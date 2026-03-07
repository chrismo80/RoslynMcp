using Microsoft.CodeAnalysis;
using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Workspace;

namespace RoslynMcp.Infrastructure.Agent;

public sealed class FlowTraceService(INavigationService navigationService, IRoslynSolutionAccessor solutionAccessor) : IFlowTraceService
{
    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    private readonly IRoslynSolutionAccessor _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));

    public async Task<TraceFlowResult> TraceFlowAsync(TraceFlowRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var directionValidation = request.Direction.NormalizeFlowDirection();
        if (directionValidation.Error != null)
        {
            return new TraceFlowResult(
                null,
                directionValidation.Direction,
                Math.Max(request.Depth ?? 2, 1),
                Array.Empty<CallEdge>(),
                Array.Empty<FlowTransition>(),
                directionValidation.Error);
        }

        var direction = directionValidation.Direction;
        var depth = Math.Max(request.Depth ?? 2, 1);

        var root = await ResolveRootSymbolAsync(request, ct).ConfigureAwait(false);
        if (root.Symbol == null)
        {
            return new TraceFlowResult(
                null,
                direction,
                depth,
                Array.Empty<CallEdge>(),
                Array.Empty<FlowTransition>(),
                AgentErrorInfo.Normalize(root.Error, "Call trace_flow with a resolvable symbolId or source position."));
        }

        IReadOnlyList<CallEdge> edges;
        if (string.Equals(direction, "upstream", StringComparison.Ordinal))
        {
            var callers = await _navigationService.GetCallersAsync(new GetCallersRequest(root.Symbol.SymbolId, depth), ct).ConfigureAwait(false);
            if (callers.Error != null)
            {
                return new TraceFlowResult(
                    root.Symbol,
                    direction,
                    depth,
                    Array.Empty<CallEdge>(),
                    Array.Empty<FlowTransition>(),
                    AgentErrorInfo.Normalize(callers.Error, "Retry trace_flow with a resolvable symbol and supported upstream traversal depth."));
            }

            edges = callers.Callers;
        }
        else if (string.Equals(direction, "downstream", StringComparison.Ordinal))
        {
            var callees = await _navigationService.GetCalleesAsync(new GetCalleesRequest(root.Symbol.SymbolId, depth), ct).ConfigureAwait(false);
            if (callees.Error != null)
            {
                return new TraceFlowResult(
                    root.Symbol,
                    direction,
                    depth,
                    Array.Empty<CallEdge>(),
                    Array.Empty<FlowTransition>(),
                    AgentErrorInfo.Normalize(callees.Error, "Retry trace_flow with a resolvable symbol and supported downstream traversal depth."));
            }

            edges = callees.Callees;
        }
        else
        {
            var graph = await _navigationService.GetCallGraphAsync(new GetCallGraphRequest(root.Symbol.SymbolId, "both", depth), ct).ConfigureAwait(false);
            if (graph.Error != null)
            {
                return new TraceFlowResult(
                    root.Symbol,
                    direction,
                    depth,
                    Array.Empty<CallEdge>(),
                    Array.Empty<FlowTransition>(),
                    AgentErrorInfo.Normalize(graph.Error, "Retry trace_flow with a resolvable symbol and supported traversal depth."));
            }

            edges = graph.Edges;
        }

        var filteredEdges = edges.Where(static edge => SourceVisibility.ShouldIncludeInHumanResults(edge.Location.FilePath)).ToArray();
        Dictionary<string, string>? symbolProjects = null;

        var (solution, _) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution != null)
        {
            var symbolFacts = await ResolveSymbolFactsAsync(solution, filteredEdges, ct).ConfigureAwait(false);
            filteredEdges = filteredEdges.Where(edge => ShouldIncludeEdge(edge, symbolFacts)).ToArray();
            symbolProjects = symbolFacts.ToDictionary(pair => pair.Key, pair => pair.Value.ProjectName, StringComparer.Ordinal);
        }

        var transitions = filteredEdges
            .GroupBy(edge => (
                From: symbolProjects?.GetValueOrDefault(edge.FromSymbolId, "unknown") ?? "unknown",
                To: symbolProjects?.GetValueOrDefault(edge.ToSymbolId, "unknown") ?? "unknown"))
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key.From, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.To, StringComparer.Ordinal)
            .Select(group => new FlowTransition(group.Key.From, group.Key.To, group.Count()))
            .ToArray();

        return new TraceFlowResult(root.Symbol, direction, depth, filteredEdges, transitions);
    }

    private async Task<FindSymbolResult> ResolveRootSymbolAsync(TraceFlowRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.SymbolId))
        {
            return await _navigationService.FindSymbolAsync(new FindSymbolRequest(request.SymbolId), ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
        {
            var atPosition = await _navigationService.GetSymbolAtPositionAsync(
                new GetSymbolAtPositionRequest(request.Path, request.Line.Value, request.Column.Value),
                ct).ConfigureAwait(false);
            return new FindSymbolResult(atPosition.Symbol, atPosition.Error);
        }

        return new FindSymbolResult(
            null,
            AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "Provide symbolId or path/line/column.",
                "Call trace_flow with a symbolId or source position."));
    }

    private async Task<Dictionary<string, SymbolFlowFacts>> ResolveSymbolFactsAsync(Solution solution, IReadOnlyList<CallEdge> edges, CancellationToken ct)
    {
        var symbolIds = edges
            .SelectMany(static edge => new[] { edge.FromSymbolId, edge.ToSymbolId })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var result = new Dictionary<string, SymbolFlowFacts>(StringComparer.Ordinal);
        foreach (var symbolId in symbolIds)
        {
            var facts = await ResolveSymbolFactsAsync(solution, symbolId, ct).ConfigureAwait(false);
            if (facts != null)
            {
                result[symbolId] = facts;
            }
        }

        return result;
    }

    private async Task<SymbolFlowFacts?> ResolveSymbolFactsAsync(Solution solution, string symbolId, CancellationToken ct)
    {
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                continue;
            }

            var symbol = SymbolIdentity.Resolve(symbolId, compilation, ct);
            if (symbol == null)
            {
                continue;
            }

            var normalized = symbol.OriginalDefinition ?? symbol;
            var sourcePath = normalized.Locations.FirstOrDefault(static location => location.IsInSource)?.GetLineSpan().Path;
            if (!SourceVisibility.ShouldIncludeInHumanResults(sourcePath))
            {
                return null;
            }

            return new SymbolFlowFacts(normalized.ResolveProjectName(project), sourcePath);
        }

        return null;
    }

    private static bool ShouldIncludeEdge(CallEdge edge, IReadOnlyDictionary<string, SymbolFlowFacts> symbolFacts)
        => symbolFacts.ContainsKey(edge.FromSymbolId)
           && symbolFacts.ContainsKey(edge.ToSymbolId)
           && SourceVisibility.ShouldIncludeInHumanResults(symbolFacts[edge.FromSymbolId].DeclarationPath)
           && SourceVisibility.ShouldIncludeInHumanResults(symbolFacts[edge.ToSymbolId].DeclarationPath);

    private sealed record SymbolFlowFacts(string ProjectName, string? DeclarationPath);
}
