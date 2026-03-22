using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMcp.Tools.Infrastructure;
using RoslynMcp.Tools.Infrastructure.Services;
using System.Collections.Immutable;

namespace RoslynMcp.Tools.Inspection.TraceCallFlow;

public sealed class Service(RoslynMcp.Tools.Infrastructure.Services.Workspace workspace)
{
    private const string UnresolvedProjectLabel = "unresolved_project";
    private const string ProjectInferenceDegradedLabel = "project_inference_degraded";

    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        var (direction, directionError) = request.Direction.NormalizeDirection();
        var depth = Math.Max(request.Depth ?? 2, 1);
        if (directionError is not null)
            return new Result(null, null, direction, depth, null, [], null, null, null, directionError);

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
            return new Result(null, null, direction, depth, null, [], null, null, null, new ErrorInfo("no_solution_loaded", "No solution is currently loaded."));

        var workspaceRoot = Path.GetDirectoryName(session.SelectedSolutionPath) ?? Path.GetFullPath(Directory.GetCurrentDirectory());

        var (root, rootError) = await ResolveRootAsync(request, session.Solution, workspaceRoot, cancellationToken).ConfigureAwait(false);
        if (rootError is not null)
            return new Result(null, null, direction, depth, null, [], null, null, null, rootError).WithWorkspaceRelativePaths(workspaceRoot);

        var resolvedRoot = root!;

        var edges = direction switch
        {
            FlowDirections.Upstream => await resolvedRoot.GetCallersAsync(session.Solution, depth, cancellationToken).ConfigureAwait(false),
            FlowDirections.Downstream => await resolvedRoot.GetCalleesAsync(session.Solution, depth, cancellationToken).ConfigureAwait(false),
            _ =>

            [
                .. (await resolvedRoot.GetCallersAsync(session.Solution, depth, cancellationToken).ConfigureAwait(false))
,
                .. await resolvedRoot.GetCalleesAsync(session.Solution, depth, cancellationToken).ConfigureAwait(false),
            ]
        };

        var filteredEdges = edges.Where(static edge => SourceVisibility.ShouldIncludeInInteractiveTrace(edge.Location.FilePath)).ToArray();
        var rootUncertaintyCategories = await DetectRootBlindspotsAsync(resolvedRoot, session.Solution, cancellationToken).ConfigureAwait(false);

        var symbolFacts = await ResolveSymbolFactsAsync(session.Solution, filteredEdges, cancellationToken).ConfigureAwait(false);
        filteredEdges = filteredEdges.Where(edge => ShouldIncludeEdge(edge, symbolFacts)).ToArray();

        var enrichedEdges = await EnrichEdgesAsync(session.Solution, filteredEdges, cancellationToken).ConfigureAwait(false);
        var possibleTargetEdges = request.IncludePossibleTargets ? BuildPossibleTargetEdges(enrichedEdges) : [];

        var edgeList = enrichedEdges.Select(ToTraceFlowEdge).ToArray();
        var possibleTargetEdgeList = possibleTargetEdges.Select(ToTraceFlowEdge).ToArray();
        var symbolTable = enrichedEdges
            .SelectMany(static edge => new[] { edge.From, edge.To })
            .Concat(possibleTargetEdges.SelectMany(static edge => new[] { edge.From, edge.To }))
            .Append(resolvedRoot)
            .GroupBy(static symbol => symbol.ToStableId(), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().ToTraceSymbolEntry(), StringComparer.Ordinal);

        var transitions = enrichedEdges
            .GroupBy(edge =>
            {
                var fromProject = symbolFacts.GetValueOrDefault(edge.From.ToStableId())?.ProjectName ?? UnresolvedProjectLabel;
                var toProject = symbolFacts.GetValueOrDefault(edge.To.ToStableId())?.ProjectName ?? UnresolvedProjectLabel;
                return (From: fromProject, To: toProject);
            })
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key.From, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.To, StringComparer.Ordinal)
            .Select(group => new FlowTransition(group.Key.From, group.Key.To, group.Count(), GetTransitionUncertaintyCategories(group.Key.From, group.Key.To)))
            .ToArray();

        return new Result(resolvedRoot.ToStableId(), resolvedRoot.ToRootSummary(), direction, depth, symbolTable, edgeList, possibleTargetEdgeList.Length == 0 ? null : possibleTargetEdgeList, transitions.Length == 0 ? null : transitions, rootUncertaintyCategories.Count == 0 ? null : rootUncertaintyCategories, null).WithWorkspaceRelativePaths(workspaceRoot);
    }

    private static IReadOnlyList<(ISymbol From, ISymbol To, SourceLocation Location, IReadOnlyList<string>? UncertaintyCategories)> BuildPossibleTargetEdges(
        IReadOnlyList<(ISymbol From, ISymbol To, SourceLocation Location, IReadOnlyList<string>? UncertaintyCategories, IReadOnlyList<ISymbol> PossibleTargets)> edges)
    {
        var unique = new Dictionary<string, (ISymbol From, ISymbol To, SourceLocation Location, IReadOnlyList<string>? UncertaintyCategories)>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            foreach (var target in edge.PossibleTargets)
            {
                unique[$"{edge.From.ToStableId()}->{target.ToStableId()}@{edge.Location.FilePath}:{edge.Location.Line}:{edge.Location.Column}"] = (edge.From, target, edge.Location, edge.UncertaintyCategories);
            }
        }

        return [.. unique.Values.OrderBy(static edge => edge.Location.FilePath, StringComparer.Ordinal).ThenBy(static edge => edge.Location.Line).ThenBy(static edge => edge.Location.Column).ThenBy(static edge => edge.To.ToStableId(), StringComparer.Ordinal)];
    }

    private async Task<(ISymbol? Symbol, ErrorInfo? Error)> ResolveRootAsync(Request request, Solution solution, string workspaceRoot, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SymbolId))
        {
            var symbol = await SymbolLookup.ResolveSymbolAsync(request.SymbolId!, solution, cancellationToken).ConfigureAwait(false);
            return symbol is null
                ? (null, new ErrorInfo("symbol_not_found", $"Symbol '{request.SymbolId}' could not be resolved."))
                : (symbol, null);
        }

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
        {
            var symbol = await SymbolLookup.GetSymbolAtPositionAsync(solution, request.Path!, request.Line.Value, request.Column.Value, workspaceRoot, cancellationToken).ConfigureAwait(false);
            return symbol is null
                ? (null, new ErrorInfo("symbol_not_found", "No symbol was found at the requested source position."))
                : (symbol, null);
        }

        return (null, new ErrorInfo("invalid_input", "Call trace_call_flow with a resolvable symbolId or source position."));
    }

    private static TraceFlowEdge ToTraceFlowEdge((ISymbol From, ISymbol To, SourceLocation Location, IReadOnlyList<string>? UncertaintyCategories, IReadOnlyList<ISymbol> PossibleTargets) edge)
        => new(edge.From.ToStableId(), edge.To.ToStableId(), edge.Location, FlowEvidenceKinds.DirectStatic, edge.UncertaintyCategories);

    private static TraceFlowEdge ToTraceFlowEdge((ISymbol From, ISymbol To, SourceLocation Location, IReadOnlyList<string>? UncertaintyCategories) edge)
        => new(edge.From.ToStableId(), edge.To.ToStableId(), edge.Location, FlowEvidenceKinds.PossibleTarget, edge.UncertaintyCategories);

    private async Task<IReadOnlyList<(ISymbol From, ISymbol To, SourceLocation Location, IReadOnlyList<string>? UncertaintyCategories, IReadOnlyList<ISymbol> PossibleTargets)>> EnrichEdgesAsync(
        Solution solution,
        IReadOnlyList<(ISymbol From, ISymbol To, SourceLocation Location)> edges,
        CancellationToken cancellationToken)
    {
        var enriched = new List<(ISymbol From, ISymbol To, SourceLocation Location, IReadOnlyList<string>? UncertaintyCategories, IReadOnlyList<ISymbol> PossibleTargets)>(edges.Count);
        foreach (var edge in edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uncertaintyCategories = new List<string>();
            var possibleTargets = new List<ISymbol>();

            if (edge.To is IMethodSymbol method)
            {
                if (method.ContainingType?.TypeKind == TypeKind.Interface)
                {
                    uncertaintyCategories.Add(FlowUncertaintyCategories.InterfaceDispatch);
                    possibleTargets.AddRange(await FindPossibleTargetsAsync(method, solution, cancellationToken).ConfigureAwait(false));
                }
                else if (CanHavePolymorphicTargets(method))
                {
                    var implementations = await FindPossibleTargetsAsync(method, solution, cancellationToken).ConfigureAwait(false);
                    if (implementations.Count > 0)
                    {
                        uncertaintyCategories.Add(FlowUncertaintyCategories.PolymorphicInference);
                        possibleTargets.AddRange(implementations);
                    }
                }
            }

            enriched.Add((edge.From, edge.To, edge.Location, uncertaintyCategories.Count == 0 ? null : uncertaintyCategories, possibleTargets));
        }

        return enriched;
    }

    private async Task<IReadOnlyDictionary<string, SymbolFacts>> ResolveSymbolFactsAsync(Solution solution, IReadOnlyList<(ISymbol From, ISymbol To, SourceLocation Location)> edges, CancellationToken cancellationToken)
    {
        var symbolIds = edges.SelectMany(static edge => new[] { edge.From.ToStableId(), edge.To.ToStableId() }).Distinct(StringComparer.Ordinal).ToArray();
        var facts = new Dictionary<string, SymbolFacts>(StringComparer.Ordinal);
        foreach (var symbolId in symbolIds)
        {
            var symbol = await SymbolLookup.ResolveSymbolAsync(symbolId, solution, cancellationToken).ConfigureAwait(false);
            if (symbol is null)
                continue;

            var declarationPaths = symbol.Locations.Where(static location => location.IsInSource).Select(static location => location.GetLineSpan().Path).Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (declarationPaths.Length == 0)
                continue;

            var projectNames = declarationPaths
                .Select(path => solution.Projects.SelectMany(static project => project.Documents).FirstOrDefault(document => document.FilePath.MatchesByNormalizedPath(path))?.Project.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var declarationPath = declarationPaths.OrderBy(static path => SourceVisibility.ShouldIncludeInInteractiveTrace(path) ? 0 : 1).ThenBy(static path => path, StringComparer.Ordinal).First();
            if (!SourceVisibility.ShouldIncludeInInteractiveTrace(declarationPath))
                continue;

            var projectName = projectNames.Length switch
            {
                1 => projectNames[0]!,
                > 1 => ProjectInferenceDegradedLabel,
                _ => UnresolvedProjectLabel
            };

            facts[symbolId] = new SymbolFacts(projectName, declarationPath);
        }

        return facts;
    }

    private static bool ShouldIncludeEdge((ISymbol From, ISymbol To, SourceLocation Location) edge, IReadOnlyDictionary<string, SymbolFacts> symbolFacts)
        => symbolFacts.ContainsKey(edge.From.ToStableId())
           && symbolFacts.ContainsKey(edge.To.ToStableId())
           && SourceVisibility.ShouldIncludeInInteractiveTrace(edge.Location.FilePath);

    private static IReadOnlyList<string> GetTransitionUncertaintyCategories(string fromProject, string toProject)
    {
        var categories = new List<string>();
        if (string.Equals(fromProject, UnresolvedProjectLabel, StringComparison.Ordinal) || string.Equals(toProject, UnresolvedProjectLabel, StringComparison.Ordinal))
            categories.Add(FlowUncertaintyCategories.UnresolvedProject);
        if (string.Equals(fromProject, ProjectInferenceDegradedLabel, StringComparison.Ordinal) || string.Equals(toProject, ProjectInferenceDegradedLabel, StringComparison.Ordinal))
            categories.Add(FlowUncertaintyCategories.ProjectInferenceDegraded);
        return categories;
    }

    private static bool CanHavePolymorphicTargets(IMethodSymbol method)
        => method.IsAbstract || ((method.IsVirtual || method.IsOverride) && !method.IsSealed);

    private static async Task<IReadOnlyList<ISymbol>> FindPossibleTargetsAsync(IMethodSymbol method, Solution solution, CancellationToken cancellationToken)
    {
        var implementations = await SymbolFinder.FindImplementationsAsync(method.OriginalDefinition, solution, solution.Projects.ToImmutableHashSet(), cancellationToken).ConfigureAwait(false);
        return [.. implementations
            .OfType<IMethodSymbol>()
            .Where(static implementation => !implementation.IsAbstract)
            .Where(static implementation => implementation.ContainingType is null || !implementation.ContainingType.IsAbstract)
            .Where(static implementation => implementation.Locations.Any(location => location.IsInSource))
            .Where(static implementation => SourceVisibility.ShouldIncludeInInteractiveTrace(implementation.GetDeclarationPosition().FilePath))
            .Select(static implementation => (ISymbol)(implementation.ConstructedFrom ?? implementation.OriginalDefinition ?? implementation))
            .DistinctBy(static implementation => implementation.ToStableId())
            .OrderBy(static implementation => implementation.ToStableId(), StringComparer.Ordinal)];
    }

    private static async Task<IReadOnlyList<string>> DetectRootBlindspotsAsync(ISymbol root, Solution solution, CancellationToken cancellationToken)
    {
        var categories = new List<string>();
        if (await UsesReflectionAsync(root, solution, cancellationToken).ConfigureAwait(false))
            categories.Add(FlowUncertaintyCategories.ReflectionBlindspot);
        if (await UsesDynamicAsync(root, solution, cancellationToken).ConfigureAwait(false))
            categories.Add(FlowUncertaintyCategories.DynamicUnresolved);
        return categories;
    }

    private static async Task<bool> UsesReflectionAsync(ISymbol root, Solution solution, CancellationToken cancellationToken)
    {
        foreach (var reference in root.DeclaringSyntaxReferences)
        {
            var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            if (node.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(static invocation => invocation.Expression.ToString().Contains("GetType", StringComparison.Ordinal) || invocation.Expression.ToString().Contains("Invoke", StringComparison.Ordinal) || invocation.Expression.ToString().Contains("GetMethod", StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private static async Task<bool> UsesDynamicAsync(ISymbol root, Solution solution, CancellationToken cancellationToken)
    {
        foreach (var reference in root.DeclaringSyntaxReferences)
        {
            var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            if (node.DescendantNodes().OfType<IdentifierNameSyntax>().Any(identifier => string.Equals(identifier.Identifier.ValueText, "dynamic", StringComparison.OrdinalIgnoreCase))
                || node.DescendantNodes().OfType<VariableDeclarationSyntax>().Any(static declaration => declaration.Type.ToString() == "dynamic"))
                return true;
        }

        return false;
    }

    private sealed record SymbolFacts(string ProjectName, string DeclarationPath);
}
