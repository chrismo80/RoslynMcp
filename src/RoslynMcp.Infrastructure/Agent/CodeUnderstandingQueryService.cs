using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Agent;

/// <summary>
/// Query service for code understanding: resolves solutions, symbols, and provides auto-bootstrap.
/// Used by all handlers to access Roslyn solution data.
/// </summary>
internal sealed class CodeUnderstandingQueryService(
    IRoslynSolutionAccessor solutionAccessor,
    ISolutionSessionService solutionSessionService,
    IWorkspaceBootstrapService workspaceBootstrapService,
    ISymbolLookupService symbolLookupService,
    INavigationService navigationService)
{
    public async Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionWithAutoBootstrapAsync(
        string noSolutionNextAction,
        string? workspaceHintPath,
        CancellationToken ct)
    {
        var (solution, error) = await solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution != null)
        {
            return (solution, null);
        }

        var discoveryRoot = workspaceHintPath.ResolveDiscoveryRoot();
        var discovered = await solutionSessionService
            .DiscoverSolutionsAsync(new DiscoverSolutionsRequest(discoveryRoot), ct)
            .ConfigureAwait(false);

        if (discovered.Error != null || discovered.SolutionPaths.Count != 1)
        {
            return (null, AgentErrorInfo.Normalize(error, noSolutionNextAction));
        }

        var load = await workspaceBootstrapService
            .LoadSolutionAsync(new LoadSolutionRequest(discovered.SolutionPaths[0]), ct)
            .ConfigureAwait(false);

        if (load.Error != null)
        {
            return (null, AgentErrorInfo.Normalize(load.Error, noSolutionNextAction));
        }

        var (autoLoadedSolution, autoLoadedError) = await solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (autoLoadedSolution == null)
        {
            return (null, AgentErrorInfo.Normalize(autoLoadedError ?? error, noSolutionNextAction));
        }

        return (autoLoadedSolution, null);
    }

    public async Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(
        string noSolutionNextAction,
        CancellationToken ct)
    {
        var (solution, error) = await solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution != null)
        {
            return (solution, null);
        }

        return (null, AgentErrorInfo.Normalize(error, noSolutionNextAction));
    }

    public async Task<IReadOnlyList<HotspotSummary>> BuildHotspotsAsync(
        Solution solution,
        IReadOnlyList<MetricItem> metrics,
        int hotspotCount,
        CancellationToken ct)
    {
        var ranked = metrics
            .OrderByDescending(static m => m.CyclomaticComplexity ?? 0)
            .ThenByDescending(static m => m.LineCount ?? 0)
            .ThenBy(static m => m.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var hotspots = new List<HotspotSummary>(hotspotCount);
        foreach (var metric in ranked)
        {
            if (hotspots.Count >= hotspotCount)
            {
                break;
            }

            var complexity = metric.CyclomaticComplexity ?? 0;
            var lineCount = metric.LineCount ?? 0;
            var score = complexity + lineCount;

            var symbol = await symbolLookupService.ResolveSymbolAsync(metric.SymbolId, solution, ct).ConfigureAwait(false);
            var displayName = symbol?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? metric.SymbolId;
            var (filePath, startLine, startColumn, _, _) = symbol.GetSourceSpan();
            if (!SourceVisibility.ShouldIncludeInHumanResults(filePath))
            {
                continue;
            }

            var reason = $"complexity={complexity}, lines={lineCount}";
            if (string.IsNullOrWhiteSpace(filePath))
            {
                reason += ", location=unknown";
            }

            hotspots.Add(new HotspotSummary(
                Display: displayName,
                Reason: reason,
                Score: score,
                SymbolId: metric.SymbolId,
                Location: CreateOptionalSourceLocation(filePath, startLine, startColumn),
                Complexity: complexity,
                LineCount: lineCount));
        }

        return hotspots
            .OrderByDescending(static h => h.Score)
            .ThenBy(static h => h.SymbolId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<GetSymbolAtPositionResult> ResolveSymbolAtRequestAsync(
        string? symbolId,
        string? path,
        int? line,
        int? column,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(symbolId))
        {
            var find = await navigationService.FindSymbolAsync(new FindSymbolRequest(symbolId), ct).ConfigureAwait(false);
            return new GetSymbolAtPositionResult(find.Symbol, find.Error);
        }

        if (!string.IsNullOrWhiteSpace(path) && line.HasValue && column.HasValue)
        {
            return await navigationService.GetSymbolAtPositionAsync(
                new GetSymbolAtPositionRequest(path, line.Value, column.Value),
                ct).ConfigureAwait(false);
        }

        return new GetSymbolAtPositionResult(
            null,
            AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "Provide symbolId or path/line/column.",
                "Call explain_symbol with a symbolId or source position."));
    }

    private static SourceLocation? CreateOptionalSourceLocation(string filePath, int? line, int? column)
        => string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue
            ? null
            : new SourceLocation(filePath, line.Value, column.Value);

    public async Task<(INamedTypeSymbol? Symbol, ErrorInfo? Error)> ResolveTypeSymbolAsync(
        ListMembersRequest request,
        Solution solution,
        CancellationToken ct)
    {
        var hasExplicitTypeSymbolId = !string.IsNullOrWhiteSpace(request.TypeSymbolId);
        ISymbol? symbol;

        if (hasExplicitTypeSymbolId)
        {
            symbol = await symbolLookupService.ResolveSymbolAsync(request.TypeSymbolId!, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return (null,
                    AgentErrorInfo.Create(
                        ErrorCodes.InvalidInput,
                        $"typeSymbolId '{request.TypeSymbolId}' could not be resolved.",
                        "Call list_types first to select a valid type symbolId.",
                        ("field", "typeSymbolId"),
                        ("provided", request.TypeSymbolId),
                        ("expected", "type symbolId returned by list_types")));
            }

            if (symbol is not INamedTypeSymbol namedType)
            {
                return (null,
                    AgentErrorInfo.Create(
                        ErrorCodes.InvalidInput,
                        "typeSymbolId must resolve to a type symbol.",
                        "Call list_types and pass a type symbolId, not a member symbolId.",
                        ("field", "typeSymbolId"),
                        ("provided", request.TypeSymbolId),
                        ("expected", "type symbolId")));
            }

            return (namedType, null);
        }

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
        {
            symbol = await symbolLookupService
                .GetSymbolAtPositionAsync(solution, request.Path!, request.Line.Value, request.Column.Value, ct)
                .ConfigureAwait(false);
            if (symbol == null)
            {
                return (null,
                    AgentErrorInfo.Create(
                        ErrorCodes.SymbolNotFound,
                        "No symbol found at the provided source position.",
                        "Call list_members with a valid typeSymbolId from list_types, or provide a valid source position."));
            }
        }
        else
        {
            return (null,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "Provide typeSymbolId or path/line/column.",
                    "Call list_members with a typeSymbolId from list_types, or provide a source position."));
        }

        var typeSymbol = symbol switch
        {
            INamedTypeSymbol namedType => namedType,
            _ => symbol.ContainingType
        };

        if (typeSymbol == null)
        {
            return (null,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "Resolved symbol is not a type and has no containing type.",
                    "Call list_members with a symbolId that resolves to a type declaration.",
                    ("field", "typeSymbolId")));
        }

        return (typeSymbol, null);
    }
}
