using Microsoft.CodeAnalysis;
using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Workspace;

namespace RoslynMcp.Infrastructure.Agent;

/// <summary>
/// Traces call flow between symbols: upstream (who calls this) and downstream (what does this call).
/// Builds call graph edges with project/namespace context.
/// </summary>
public sealed class FlowTraceService(INavigationService navigationService, IRoslynSolutionAccessor solutionAccessor) : IFlowTraceService
{
    private const string UnresolvedProjectLabel = "unresolved_project";
    private const string ProjectInferenceDegradedLabel = "project_inference_degraded";

    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    private readonly IRoslynSolutionAccessor _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));

    public async Task<TraceFlowResult> TraceFlowAsync(TraceFlowRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        
        var (direction, error) = request.Direction.NormalizeFlowDirection();
        if (error != null)
            return new TraceFlowResult(null, null, direction, Math.Max(request.Depth ?? 2, 1), null, [], null, null, null, error);

        var depth = Math.Max(request.Depth ?? 2, 1);

        var root = await ResolveRootSymbolAsync(request, ct).ConfigureAwait(false);
        if (root.Symbol == null)
            return new TraceFlowResult(null, null, direction, depth, null, [], null, null, null, AgentErrorInfo.Normalize(root.Error, "Call trace_flow with a resolvable symbolId or source position."));

        IReadOnlyList<CallEdge> edges;
        if (string.Equals(direction, "upstream", StringComparison.Ordinal))
        {
            var callers = await _navigationService.GetCallersAsync(new GetCallersRequest(root.Symbol.SymbolId, depth), ct).ConfigureAwait(false);
            if (callers.Error != null)
                return CreateErrorResult(root.Symbol, direction, depth, callers.Error, "Retry trace_flow with a resolvable symbol and supported upstream traversal depth.");

            edges = callers.Callers;
        }
        else if (string.Equals(direction, "downstream", StringComparison.Ordinal))
        {
            var callees = await _navigationService.GetCalleesAsync(new GetCalleesRequest(root.Symbol.SymbolId, depth), ct).ConfigureAwait(false);
            if (callees.Error != null)
                return CreateErrorResult(root.Symbol, direction, depth, callees.Error, "Retry trace_flow with a resolvable symbol and supported downstream traversal depth.");

            edges = callees.Callees;
        }
        else
        {
            var graph = await _navigationService.GetCallGraphAsync(new GetCallGraphRequest(root.Symbol.SymbolId, "both", depth), ct).ConfigureAwait(false);
            if (graph.Error != null)
                return CreateErrorResult(root.Symbol, direction, depth, graph.Error, "Retry trace_flow with a resolvable symbol and supported traversal depth.");

            edges = graph.Edges;
        }

        var filteredEdges = edges.Where(static edge => SourceVisibility.ShouldIncludeInInteractiveTrace(edge.Location.FilePath)).ToArray();
        Dictionary<string, string>? symbolProjects = null;
        var rootUncertaintyCategories = new HashSet<string>(StringComparer.Ordinal);

        var (solution, _) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution != null)
        {
            foreach (var category in await DetectRootBlindspotsAsync(solution, root.Symbol, ct).ConfigureAwait(false))
                rootUncertaintyCategories.Add(category);

            var symbolFacts = await ResolveSymbolFactsAsync(solution, filteredEdges, ct).ConfigureAwait(false);
            filteredEdges = filteredEdges.Where(edge => ShouldIncludeEdge(edge, symbolFacts)).ToArray();
            symbolProjects = symbolFacts.ToDictionary(pair => pair.Key, pair => pair.Value.ProjectName, StringComparer.Ordinal);
            filteredEdges = (await EnrichEdgesAsync(solution, filteredEdges, ct).ConfigureAwait(false)).ToArray();
        }

        var transitions = filteredEdges
            .GroupBy(edge => (
                From: symbolProjects?.GetValueOrDefault(edge.FromSymbolId, UnresolvedProjectLabel) ?? UnresolvedProjectLabel,
                To: symbolProjects?.GetValueOrDefault(edge.ToSymbolId, UnresolvedProjectLabel) ?? UnresolvedProjectLabel))
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key.From, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.To, StringComparer.Ordinal)
            .Select(group => new FlowTransition(group.Key.From, group.Key.To, group.Count(), GetTransitionUncertaintyCategories(group.Key.From, group.Key.To)))
            .ToArray();

        var possibleTargetEdges = request.IncludePossibleTargets ? BuildPossibleTargetEdges(filteredEdges) : [];

        var symbols = BuildSymbolTable(root.Symbol, filteredEdges, possibleTargetEdges);
        return new TraceFlowResult(
            root.Symbol.SymbolId,
            new TraceRootSummary(root.Symbol.Name, root.Symbol.Kind, root.Symbol.ContainingType ?? NormalizeOwner(root.Symbol.ContainingNamespace), root.Symbol.DeclarationLocation),
            direction,
            depth,
            symbols,
            filteredEdges.Select(ToTraceFlowEdge).ToArray(),
            request.IncludePossibleTargets && possibleTargetEdges.Count > 0 ? possibleTargetEdges.Select(ToTraceFlowEdge).ToArray() : null,
            transitions.Length == 0 ? null : transitions,
            rootUncertaintyCategories.Count == 0 ? null : rootUncertaintyCategories.OrderBy(static category => category, StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<CallEdge> BuildPossibleTargetEdges(IReadOnlyList<CallEdge> edges)
    {
        if (edges.Count == 0)
            return [];

        var unique = new Dictionary<string, CallEdge>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (edge.PossibleTargets is null || edge.PossibleTargets.Count == 0)
                continue;

            foreach (var target in edge.PossibleTargets)
            {
                var possibleEdge = new CallEdge(
                    edge.FromSymbolId,
                    target.SymbolId,
                    edge.Location,
                    edge.FromReference,
                    target,
                    FlowEvidenceKinds.PossibleTarget,
                    edge.Uncertainties ?? Array.Empty<FlowUncertainty>(),
                    Array.Empty<SymbolReference>());

                unique[possibleEdge.GetEdgeKey()] = possibleEdge;
            }
        }

        return unique.Values
            .OrderBy(static edge => edge.Location, SourceLocationComparer.Instance)
            .ThenBy(static edge => edge.FromSymbolId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.ToSymbolId, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<CallEdge>> EnrichEdgesAsync(Solution solution, IReadOnlyList<CallEdge> edges, CancellationToken ct)
    {
        if (edges.Count == 0)
        {
            return edges;
        }

        var symbolCache = new Dictionary<string, ISymbol?>(StringComparer.Ordinal);
        var enriched = new List<CallEdge>(edges.Count);

        foreach (var edge in edges)
        {
            ct.ThrowIfCancellationRequested();

            var target = await ResolveSymbolAsync(solution, edge.ToSymbolId, symbolCache, ct).ConfigureAwait(false);
            if (target == null)
            {
                enriched.Add(edge);
                continue;
            }

            var uncertainties = new List<FlowUncertainty>();
            var possibleTargets = new List<SymbolReference>();

            if (target is IMethodSymbol method)
            {
                if (method.ContainingType?.TypeKind == TypeKind.Interface)
                {
                    uncertainties.Add(CreateEdgeUncertainty(
                        FlowUncertaintyCategories.InterfaceDispatch,
                        "Static analysis resolves this call to an interface member, but runtime dispatch may target any implementing member.",
                        edge.Location,
                        edge.ToReference));

                    var implementations = await FindPossibleTargetsAsync(method, solution, ct).ConfigureAwait(false);
                    possibleTargets.AddRange(implementations);
                }
                else if (CanHavePolymorphicTargets(method))
                {
                    var implementations = await FindPossibleTargetsAsync(method, solution, ct).ConfigureAwait(false);
                    if (implementations.Count > 0)
                    {
                        uncertainties.Add(CreateEdgeUncertainty(
                            FlowUncertaintyCategories.PolymorphicInference,
                            "Static analysis resolves a virtual or abstract member, but runtime dispatch may target an override or concrete implementation.",
                            edge.Location,
                            edge.ToReference));

                        possibleTargets.AddRange(implementations);
                    }
                }
            }

            enriched.Add(uncertainties.Count == 0 && possibleTargets.Count == 0
                ? edge with { Uncertainties = Array.Empty<FlowUncertainty>(), PossibleTargets = Array.Empty<SymbolReference>() }
                : edge with
                {
                    Uncertainties = uncertainties,
                    PossibleTargets = possibleTargets
                });
        }

        return enriched;
    }

    private static async Task<IReadOnlyList<string>> DetectRootBlindspotsAsync(Solution solution, SymbolDescriptor root, CancellationToken ct)
    {
        var symbol = await ResolveSymbolAsync(solution, root.SymbolId, cache: null, ct).ConfigureAwait(false);
        if (symbol == null)
        {
            return Array.Empty<string>();
        }

        var uncertainties = new List<string>();

        if (await UsesReflectionAsync(symbol, solution, ct).ConfigureAwait(false))
        {
            uncertainties.Add(FlowUncertaintyCategories.ReflectionBlindspot);
        }

        if (await UsesDynamicAsync(symbol, solution, ct).ConfigureAwait(false))
        {
            uncertainties.Add(FlowUncertaintyCategories.DynamicUnresolved);
        }

        return uncertainties;
    }

    private async Task<FindSymbolResult> ResolveRootSymbolAsync(TraceFlowRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.SymbolId))
            return await _navigationService.FindSymbolAsync(new FindSymbolRequest(request.SymbolId), ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.Path) && request is { Line: not null, Column: not null })
        {
            var atPosition = await _navigationService.GetSymbolAtPositionAsync(new GetSymbolAtPositionRequest(request.Path, request.Line.Value, request.Column.Value), ct)
                .ConfigureAwait(false);
            
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
                result[symbolId] = facts;
        }

        return result;
    }

    private async Task<SymbolFlowFacts?> ResolveSymbolFactsAsync(Solution solution, string symbolId, CancellationToken ct)
    {
        var normalizedSymbolId = NormalizeInputSymbolId(symbolId);
        if (string.IsNullOrWhiteSpace(normalizedSymbolId))
        {
            return null;
        }

        var sourceProjectNames = new HashSet<string>(StringComparer.Ordinal);
        string? declarationPath = null;
        var resolvedWithoutSource = false;

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
                continue;

            var symbol = SymbolIdentity.Resolve(normalizedSymbolId, compilation, ct);
            if (symbol == null)
                continue;

            var normalized = symbol.OriginalDefinition ?? symbol;

            var sourceLocations = normalized.Locations
                .Where(static location => location.IsInSource && location.SourceTree is not null)
                .ToArray();

            if (sourceLocations.Length == 0)
            {
                resolvedWithoutSource = true;
                continue;
            }

            declarationPath ??= SelectDeclarationPath(sourceLocations);

            foreach (var location in sourceLocations)
            {
                var document = solution.GetDocument(location.SourceTree!);
                if (document != null)
                    sourceProjectNames.Add(document.Project.Name);
            }
        }

        if (string.IsNullOrWhiteSpace(declarationPath))
            return resolvedWithoutSource ? new SymbolFlowFacts(UnresolvedProjectLabel, null) : null;

        if (!SourceVisibility.ShouldIncludeInInteractiveTrace(declarationPath))
            return null;

        return sourceProjectNames.Count switch
        {
            1 => new SymbolFlowFacts(sourceProjectNames.Single(), declarationPath),
            > 1 => new SymbolFlowFacts(ProjectInferenceDegradedLabel, declarationPath),
            _ => new SymbolFlowFacts(UnresolvedProjectLabel, declarationPath)
        };
    }

    private static bool ShouldIncludeEdge(CallEdge edge, IReadOnlyDictionary<string, SymbolFlowFacts> symbolFacts)
        => symbolFacts.ContainsKey(edge.FromSymbolId)
           && symbolFacts.ContainsKey(edge.ToSymbolId)
           && SourceVisibility.ShouldIncludeInInteractiveTrace(symbolFacts[edge.FromSymbolId].DeclarationPath)
           && SourceVisibility.ShouldIncludeInInteractiveTrace(symbolFacts[edge.ToSymbolId].DeclarationPath);

    private static IReadOnlyList<string> GetTransitionUncertaintyCategories(string fromProject, string toProject)
    {
        var categories = new HashSet<string>(StringComparer.Ordinal);

        if (string.Equals(fromProject, UnresolvedProjectLabel, StringComparison.Ordinal)
            || string.Equals(toProject, UnresolvedProjectLabel, StringComparison.Ordinal))
        {
            categories.Add(FlowUncertaintyCategories.UnresolvedProject);
        }

        if (string.Equals(fromProject, ProjectInferenceDegradedLabel, StringComparison.Ordinal)
            || string.Equals(toProject, ProjectInferenceDegradedLabel, StringComparison.Ordinal))
        {
            categories.Add(FlowUncertaintyCategories.ProjectInferenceDegraded);
        }

        return categories.Count == 0 ? [] : categories.OrderBy(static category => category, StringComparer.Ordinal).ToArray();
    }

    private static async Task<ISymbol?> ResolveSymbolAsync(
        Solution solution,
        string symbolId,
        Dictionary<string, ISymbol?>? cache,
        CancellationToken ct)
    {
        if (cache != null && cache.TryGetValue(symbolId, out var cached))
            return cached;

        var normalizedSymbolId = NormalizeInputSymbolId(symbolId);
        if (string.IsNullOrWhiteSpace(normalizedSymbolId))
        {
            cache?.Add(symbolId, null);
            return null;
        }

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            
            if (await project.GetCompilationAsync(ct).ConfigureAwait(false) is not { } compilation)
                continue;

            if (SymbolIdentity.Resolve(normalizedSymbolId, compilation, ct) is not { } symbol)
                continue;

            var resolved = symbol.OriginalDefinition ?? symbol;
            cache?.Add(symbolId, resolved);
            
            return resolved;
        }

        cache?.Add(symbolId, null);
        return null;
    }

    private static bool CanHavePolymorphicTargets(IMethodSymbol method)
        => method.IsAbstract || ((method.IsVirtual || method.IsOverride) && !method.IsSealed);

    private static string? NormalizeInputSymbolId(string? symbolId)
    {
        if (string.IsNullOrWhiteSpace(symbolId))
        {
            return null;
        }

        return symbolId.TryToInternal(out var internalSymbolId) ? internalSymbolId : symbolId;
    }

    private async Task<IReadOnlyList<SymbolReference>> FindPossibleTargetsAsync(IMethodSymbol method, Solution solution, CancellationToken ct)
    {
        var implementations = await PolymorphicImplementationDiscovery.FindImplementationSymbolsAsync(method, solution, ct).ConfigureAwait(false);
        return implementations
            .Where(static implementation => implementation.Kind == SymbolKind.Method)
            .Where(static implementation => implementation.Locations.Any(location => location.IsInSource))
            .Where(static implementation => SourceVisibility.ShouldIncludeInInteractiveTrace(implementation.GetDeclarationPosition().FilePath))
            .OrderBy(static implementation => implementation.CreateId(), StringComparer.Ordinal)
            .Select(static implementation => implementation.ToSymbolReference())
            .DistinctBy(static reference => reference.SymbolId)
            .ToArray();
    }

    private static FlowUncertainty CreateEdgeUncertainty(string category, string message, SourceLocation location, SymbolReference? relatedSymbol)
        => new(category, message, location, relatedSymbol);

    private static async Task<bool> UsesReflectionAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            var node = await syntaxReference.GetSyntaxAsync(ct).ConfigureAwait(false);
            
            if(solution.GetDocument(node.SyntaxTree) is not {} document)
                continue;

            if(await document.GetSemanticModelAsync(ct).ConfigureAwait(false)is not {} model)
                continue;

            foreach (var invocation in node.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
            {
                if(model.GetSymbolInfo(invocation, ct).Symbol  as IMethodSymbol is not {} target)
                    continue;

                var containingType = target.ContainingType?.ToDisplayString();
                if (containingType is "System.Type" or "System.Reflection.MethodInfo" or "System.Reflection.PropertyInfo" or "System.Reflection.FieldInfo" or "System.Activator")
                    return true;
            }
        }

        return false;
    }

    private static async Task<bool> UsesDynamicAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            var node = await syntaxReference.GetSyntaxAsync(ct).ConfigureAwait(false);
            
            if(solution.GetDocument(node.SyntaxTree) is not {}  document)
                continue;

            if(await document.GetSemanticModelAsync(ct).ConfigureAwait(false) is not {} model)
                continue;

            foreach (var descendant in node.DescendantNodesAndSelf())
            {
                if (model.GetTypeInfo(descendant, ct).Type?.TypeKind == TypeKind.Dynamic)
                    return true;
            }
        }

        return false;
    }

    private static string? SelectDeclarationPath(IReadOnlyList<Location> locations)
    {
        return locations
            .Select(static location => location.GetLineSpan().Path)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(static path => SourceVisibility.ShouldIncludeInInteractiveTrace(path) ? 0 : 1)
            .ThenBy(static path => SourceVisibility.ShouldIncludeInHumanResults(path) ? 0 : 1)
            .ThenBy(static path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private sealed record SymbolFlowFacts(string ProjectName, string? DeclarationPath);

    private static TraceFlowResult CreateErrorResult(SymbolDescriptor root, string direction, int depth, ErrorInfo? error, string nextAction)
        => new(root.SymbolId,
            new TraceRootSummary(root.Name, root.Kind, root.ContainingType ?? NormalizeOwner(root.ContainingNamespace), root.DeclarationLocation),
            direction,
            depth,
            null,
            [],
            null,
            null,
            null,
            AgentErrorInfo.Normalize(error, nextAction));

    private static IReadOnlyDictionary<string, TraceSymbolEntry> BuildSymbolTable(SymbolDescriptor root, IReadOnlyList<CallEdge> edges, IReadOnlyList<CallEdge> possibleTargetEdges)
    {
        var symbols = new Dictionary<string, TraceSymbolEntry>(StringComparer.Ordinal)
        {
            [root.SymbolId] = new TraceSymbolEntry(BuildRootDisplay(root), root.DeclarationLocation)
        };

        foreach (var edge in edges.Concat(possibleTargetEdges))
        {
            AddSymbol(symbols, edge.FromSymbolId, edge.FromReference);
            AddSymbol(symbols, edge.ToSymbolId, edge.ToReference);
        }

        return symbols;
    }

    private static void AddSymbol(IDictionary<string, TraceSymbolEntry> symbols, string symbolId, SymbolReference? reference)
    {
        if (symbols.ContainsKey(symbolId))
            return;

        var display = reference?.QualifiedDisplayName;
        if (string.IsNullOrWhiteSpace(display))
            return;

        symbols[symbolId] = new TraceSymbolEntry(display, reference?.DeclarationLocation);
    }

    private static TraceFlowEdge ToTraceFlowEdge(CallEdge edge)
        => new(
            edge.FromSymbolId,
            edge.ToSymbolId,
            edge.Location,
            edge.EvidenceKind,
            edge.Uncertainties is { Count: > 0 }
                ? edge.Uncertainties.Select(static uncertainty => uncertainty.Category).Distinct(StringComparer.Ordinal).OrderBy(static category => category, StringComparer.Ordinal).ToArray()
                : null,
            edge.Uncertainties is { Count: > 0 }
                ? edge.Uncertainties.Where(static uncertainty => uncertainty.RelatedSymbol != null).Select(static uncertainty => uncertainty.RelatedSymbol!.SymbolId).Distinct(StringComparer.Ordinal).ToArray()
                : null);

    private static string BuildRootDisplay(SymbolDescriptor root)
        => string.IsNullOrWhiteSpace(root.ContainingType)
            ? root.Name
            : $"{root.ContainingType}.{root.Name}";

    private static string? NormalizeOwner(string? owner)
        => string.IsNullOrWhiteSpace(owner) || string.Equals(owner, "<global namespace>", StringComparison.Ordinal)
            ? null
            : owner;
}
