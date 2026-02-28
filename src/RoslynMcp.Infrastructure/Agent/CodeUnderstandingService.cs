using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Analysis;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Collections.Immutable;

namespace RoslynMcp.Infrastructure.Agent;

public sealed class CodeUnderstandingService : ICodeUnderstandingService
{
    private const int DefaultPageSize = 100;
    private const int MaximumPageSize = 500;

    private readonly IRoslynSolutionAccessor _solutionAccessor;
    private readonly ISolutionSessionService _solutionSessionService;
    private readonly IWorkspaceBootstrapService _workspaceBootstrapService;
    private readonly IAnalysisService _analysisService;
    private readonly INavigationService _navigationService;
    private readonly ISymbolLookupService _symbolLookupService;

    public CodeUnderstandingService(
        IRoslynSolutionAccessor solutionAccessor,
        ISolutionSessionService solutionSessionService,
        IWorkspaceBootstrapService workspaceBootstrapService,
        IAnalysisService analysisService,
        INavigationService navigationService,
        ISymbolLookupService symbolLookupService)
    {
        _solutionAccessor = solutionAccessor;
        _solutionSessionService = solutionSessionService;
        _workspaceBootstrapService = workspaceBootstrapService;
        _analysisService = analysisService;
        _navigationService = navigationService;
        _symbolLookupService = symbolLookupService;
    }

    public async Task<UnderstandCodebaseResult> UnderstandCodebaseAsync(UnderstandCodebaseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = NormalizeProfile(request.Profile);

        var (solution, error) = await GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before understanding the codebase.",
            null,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new UnderstandCodebaseResult(
                profile,
                Array.Empty<ModuleSummary>(),
                Array.Empty<HotspotSummary>(),
                AgentErrorInfo.Normalize(error, "Call load_solution first to select a solution before understanding the codebase."));
        }

        var modules = solution.Projects
            .Select(project =>
            {
                var outgoing = project.ProjectReferences.Count();
                var incoming = solution.Projects.Count(otherProject =>
                    otherProject.ProjectReferences.Any(reference => reference.ProjectId == project.Id));
                return new ModuleSummary(project.Name, project.FilePath, outgoing, incoming);
            })
            .OrderByDescending(static m => m.IncomingDependencies + m.OutgoingDependencies)
            .ThenBy(static m => m.Name, StringComparer.Ordinal)
            .ToArray();

        var metricResult = await _analysisService.GetCodeMetricsAsync(new GetCodeMetricsRequest(), ct).ConfigureAwait(false);
        var hotspotCount = profile switch
        {
            "quick" => 3,
            "deep" => 10,
            _ => 5
        };

        var hotspots = await BuildHotspotsAsync(solution, metricResult.Metrics, hotspotCount, ct).ConfigureAwait(false);
        return new UnderstandCodebaseResult(
            profile,
            modules,
            hotspots,
            AgentErrorInfo.Normalize(metricResult.Error, "Run understand_codebase again after diagnostics/metrics collection succeeds."));
    }

    public async Task<ExplainSymbolResult> ExplainSymbolAsync(ExplainSymbolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (_, bootstrapError) = await GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before explaining symbols.",
            request.Path,
            ct).ConfigureAwait(false);
        if (bootstrapError != null)
        {
            return new ExplainSymbolResult(
                null,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<ImpactHint>(),
                bootstrapError);
        }

        var symbolResult = await ResolveSymbolAtRequestAsync(request.SymbolId, request.Path, request.Line, request.Column, ct).ConfigureAwait(false);
        if (symbolResult.Symbol == null)
        {
            return new ExplainSymbolResult(null,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<ImpactHint>(),
                AgentErrorInfo.Normalize(symbolResult.Error, "Call explain_symbol with symbolId or path+line+column for an existing symbol."));
        }

        var signature = await _navigationService.GetSignatureAsync(new GetSignatureRequest(symbolResult.Symbol.SymbolId), ct).ConfigureAwait(false);
        var outline = await _navigationService.GetSymbolOutlineAsync(new GetSymbolOutlineRequest(symbolResult.Symbol.SymbolId, 1), ct).ConfigureAwait(false);
        var references = await _navigationService.FindReferencesAsync(new FindReferencesRequest(symbolResult.Symbol.SymbolId), ct).ConfigureAwait(false);

        var keyReferences = references.References
            .Take(5)
            .Select(static r => $"{r.FilePath}:{r.Line}:{r.Column}")
            .ToArray();

        var impactHints = references.References
            .GroupBy(static r => Path.GetFileName(r.FilePath), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(group => new ImpactHint(group.Key ?? string.Empty, "high reference density", group.Count()))
            .ToArray();

        var roleSummary = outline.Members.Count == 0
            ? $"{symbolResult.Symbol.Kind} '{symbolResult.Symbol.Name}'."
            : $"{symbolResult.Symbol.Kind} '{symbolResult.Symbol.Name}' with {outline.Members.Count} top-level members.";

        return new ExplainSymbolResult(
            symbolResult.Symbol,
            roleSummary,
            signature.Signature,
            keyReferences,
            impactHints,
            AgentErrorInfo.Normalize(signature.Error ?? outline.Error ?? references.Error,
                "Retry explain_symbol for a resolvable symbol in the loaded solution."));
    }

    public async Task<ListTypesResult> ListTypesAsync(ListTypesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before listing types.",
            request.ProjectPath,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ListTypesResult(
                Array.Empty<TypeListEntry>(),
                0,
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before listing types."));
        }

        if (!TryNormalizeTypeKind(request.Kind, out var normalizedKind))
        {
            return new ListTypesResult(
                Array.Empty<TypeListEntry>(),
                0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "kind must be one of: class, record, interface, enum, struct.",
                    "Retry list_types with a supported kind filter or omit kind.",
                    ("field", "kind"),
                    ("provided", request.Kind ?? string.Empty),
                    ("expected", "class|record|interface|enum|struct")));
        }

        if (!TryNormalizeAccessibility(request.Accessibility, out var normalizedAccessibility))
        {
            return new ListTypesResult(
                Array.Empty<TypeListEntry>(),
                0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "accessibility must be one of: public, internal, protected, private, protected_internal, private_protected.",
                    "Retry list_types with a supported accessibility filter or omit accessibility.",
                    ("field", "accessibility"),
                    ("provided", request.Accessibility ?? string.Empty)));
        }

        var selectedProjects = ResolveProjectSelector(
            solution,
            request.ProjectPath,
            request.ProjectName,
            request.ProjectId,
            selectorRequired: true,
            toolName: "list_types",
            out var selectorError);

        if (selectorError != null)
        {
            return new ListTypesResult(Array.Empty<TypeListEntry>(), 0, selectorError);
        }

        var namespacePrefix = NormalizeOptional(request.NamespacePrefix);
        var entries = new List<TypeListEntry>();

        foreach (var project in selectedProjects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                continue;
            }

            foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
            {
                if (!type.Locations.Any(static location => location.IsInSource))
                {
                    continue;
                }

                var kind = ToTypeKind(type);
                if (kind == null)
                {
                    continue;
                }

                if (normalizedKind != null && !string.Equals(kind, normalizedKind, StringComparison.Ordinal))
                {
                    continue;
                }

                var accessibility = NormalizeAccessibility(type.DeclaredAccessibility);
                if (normalizedAccessibility != null && !string.Equals(accessibility, normalizedAccessibility, StringComparison.Ordinal))
                {
                    continue;
                }

                var typeNamespace = NormalizeNamespace(type.ContainingNamespace);
                if (namespacePrefix != null && !typeNamespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var (filePath, line, column) = GetDeclarationPosition(type);
                entries.Add(new TypeListEntry(
                    type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    SymbolIdentity.CreateId(type),
                    filePath,
                    line,
                    column,
                    kind,
                    IsPartial(type),
                    type.Arity > 0 ? type.Arity : null));
            }
        }

        var ordered = entries
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Arity ?? 0)
            .ThenBy(static item => item.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var (offset, limit) = NormalizePaging(request.Offset, request.Limit);
        var paged = ordered.Skip(offset).Take(limit).ToArray();
        return new ListTypesResult(paged, ordered.Length);
    }

    public async Task<ListMembersResult> ListMembersAsync(ListMembersRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before listing members.",
            request.Path,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ListMembersResult(
                Array.Empty<MemberListEntry>(),
                0,
                request.IncludeInherited,
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before listing members."));
        }

        if (!TryNormalizeMemberKind(request.Kind, out var normalizedMemberKind))
        {
            return new ListMembersResult(
                Array.Empty<MemberListEntry>(),
                0,
                request.IncludeInherited,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "kind must be one of: method, property, field, event, ctor.",
                    "Retry list_members with a supported kind filter or omit kind.",
                    ("field", "kind"),
                    ("provided", request.Kind ?? string.Empty),
                    ("expected", "method|property|field|event|ctor")));
        }

        if (!TryNormalizeAccessibility(request.Accessibility, out var normalizedAccessibility))
        {
            return new ListMembersResult(
                Array.Empty<MemberListEntry>(),
                0,
                request.IncludeInherited,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "accessibility must be one of: public, internal, protected, private, protected_internal, private_protected.",
                    "Retry list_members with a supported accessibility filter or omit accessibility.",
                    ("field", "accessibility"),
                    ("provided", request.Accessibility ?? string.Empty)));
        }

        if (!TryNormalizeBinding(request.Binding, out var normalizedBinding))
        {
            return new ListMembersResult(
                Array.Empty<MemberListEntry>(),
                0,
                request.IncludeInherited,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "binding must be one of: static, instance.",
                    "Retry list_members with binding=static or binding=instance, or omit binding.",
                    ("field", "binding"),
                    ("provided", request.Binding ?? string.Empty),
                    ("expected", "static|instance")));
        }

        var typeSymbol = await ResolveTypeSymbolAsync(request, solution, ct).ConfigureAwait(false);
        if (typeSymbol.Error != null)
        {
            return new ListMembersResult(Array.Empty<MemberListEntry>(), 0, request.IncludeInherited, typeSymbol.Error);
        }

        var symbols = request.IncludeInherited ? CollectMembersWithInheritance(typeSymbol.Symbol!) : typeSymbol.Symbol!.GetMembers();

        var entries = symbols
            .Select(member => ToMemberEntry(member, normalizedMemberKind, normalizedAccessibility, normalizedBinding))
            .Where(static entry => entry != null)
            .Select(static entry => entry!)
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Signature, StringComparer.Ordinal)
            .ThenBy(static item => item.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var (offset, limit) = NormalizePaging(request.Offset, request.Limit);
        var paged = entries.Skip(offset).Take(limit).ToArray();
        return new ListMembersResult(paged, entries.Length, request.IncludeInherited);
    }

    public async Task<ResolveSymbolResult> ResolveSymbolAsync(ResolveSymbolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before resolving symbols.",
            request.Path ?? request.ProjectPath,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ResolveSymbolResult(
                null,
                false,
                Array.Empty<ResolveSymbolCandidate>(),
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before resolving symbols."));
        }

        if (!string.IsNullOrWhiteSpace(request.SymbolId))
        {
            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId!, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new ResolveSymbolResult(
                    null,
                    false,
                    Array.Empty<ResolveSymbolCandidate>(),
                    AgentErrorInfo.Create(
                        ErrorCodes.SymbolNotFound,
                        $"symbolId '{request.SymbolId}' could not be resolved.",
                        "Call list_types/list_members or explain_symbol first to obtain a valid symbolId.",
                        ("field", "symbolId"),
                        ("provided", request.SymbolId)));
            }

            return new ResolveSymbolResult(ToResolvedSymbol(symbol), false, Array.Empty<ResolveSymbolCandidate>());
        }

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
        {
            var symbol = await _symbolLookupService.GetSymbolAtPositionAsync(
                solution,
                request.Path!,
                request.Line.Value,
                request.Column.Value,
                ct).ConfigureAwait(false);

            if (symbol == null)
            {
                return new ResolveSymbolResult(
                    null,
                    false,
                    Array.Empty<ResolveSymbolCandidate>(),
                    AgentErrorInfo.Create(
                        ErrorCodes.SymbolNotFound,
                        "No symbol found at the provided source position.",
                        "Call resolve_symbol with a valid path+line+column or use list_types/list_members to select a symbolId.",
                        ("field", "path"),
                        ("provided", request.Path)));
            }

            return new ResolveSymbolResult(ToResolvedSymbol(symbol), false, Array.Empty<ResolveSymbolCandidate>());
        }

        if (!string.IsNullOrWhiteSpace(request.QualifiedName))
        {
            var selectedProjects = ResolveProjectSelector(
                solution,
                request.ProjectPath,
                request.ProjectName,
                request.ProjectId,
                selectorRequired: false,
                toolName: "resolve_symbol",
                out var selectorError);

            if (selectorError != null)
            {
                return new ResolveSymbolResult(null, false, Array.Empty<ResolveSymbolCandidate>(), selectorError);
            }

            var candidates = await ResolveByQualifiedNameAsync(request.QualifiedName!, selectedProjects, ct).ConfigureAwait(false);
            if (candidates.Length == 0)
            {
                return new ResolveSymbolResult(
                    null,
                    false,
                    Array.Empty<ResolveSymbolCandidate>(),
                    AgentErrorInfo.Create(
                        ErrorCodes.SymbolNotFound,
                        $"qualifiedName '{request.QualifiedName}' did not match any symbol.",
                        "Refine qualifiedName or provide projectName/projectPath/projectId to narrow the lookup.",
                        ("field", "qualifiedName"),
                        ("provided", request.QualifiedName)));
            }

            if (candidates.Length > 1)
            {
                return new ResolveSymbolResult(
                    null,
                    true,
                    candidates,
                    AgentErrorInfo.Create(
                        ErrorCodes.AmbiguousSymbol,
                        $"qualifiedName '{request.QualifiedName}' matched multiple symbols.",
                        "Select one candidate symbolId and call resolve_symbol again, or scope by projectName/projectPath/projectId.",
                        ("field", "qualifiedName"),
                        ("provided", request.QualifiedName),
                        ("candidateCount", candidates.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            }

            var selected = candidates[0];
            return new ResolveSymbolResult(
                new ResolvedSymbolSummary(selected.SymbolId, selected.DisplayName, selected.Kind, selected.FilePath, selected.Line, selected.Column),
                false,
                Array.Empty<ResolveSymbolCandidate>());
        }

        return new ResolveSymbolResult(
            null,
            false,
            Array.Empty<ResolveSymbolCandidate>(),
            AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "Provide symbolId, path+line+column, or qualifiedName.",
                "Call resolve_symbol with one selector mode: symbolId, source position, or qualifiedName."));
    }

    private async Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionWithAutoBootstrapAsync(
        string noSolutionNextAction,
        string? workspaceHintPath,
        CancellationToken ct)
    {
        var (solution, error) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution != null)
        {
            return (solution, null);
        }

        var discoveryRoot = ResolveDiscoveryRoot(workspaceHintPath);
        var discovered = await _solutionSessionService
            .DiscoverSolutionsAsync(new DiscoverSolutionsRequest(discoveryRoot), ct)
            .ConfigureAwait(false);

        if (discovered.Error != null || discovered.SolutionPaths.Count != 1)
        {
            return (null, AgentErrorInfo.Normalize(error, noSolutionNextAction));
        }

        var load = await _workspaceBootstrapService
            .LoadSolutionAsync(new LoadSolutionRequest(discovered.SolutionPaths[0]), ct)
            .ConfigureAwait(false);

        if (load.Error != null)
        {
            return (null, AgentErrorInfo.Normalize(load.Error, noSolutionNextAction));
        }

        var (autoLoadedSolution, autoLoadedError) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (autoLoadedSolution == null)
        {
            return (null, AgentErrorInfo.Normalize(autoLoadedError ?? error, noSolutionNextAction));
        }

        return (autoLoadedSolution, null);
    }

    private async Task<IReadOnlyList<HotspotSummary>> BuildHotspotsAsync(
        Solution solution,
        IReadOnlyList<MetricItem> metrics,
        int hotspotCount,
        CancellationToken ct)
    {
        var ranked = metrics
            .OrderByDescending(static m => m.CyclomaticComplexity ?? 0)
            .ThenByDescending(static m => m.LineCount ?? 0)
            .ThenBy(static m => m.SymbolId, StringComparer.Ordinal)
            .Take(hotspotCount)
            .ToArray();

        var hotspots = new List<HotspotSummary>(ranked.Length);
        foreach (var metric in ranked)
        {
            var complexity = metric.CyclomaticComplexity ?? 0;
            var lineCount = metric.LineCount ?? 0;
            var score = complexity + lineCount;

            var symbol = await _symbolLookupService.ResolveSymbolAsync(metric.SymbolId, solution, ct).ConfigureAwait(false);
            var displayName = symbol?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? metric.SymbolId;
            var (filePath, startLine, _, endLine, _) = GetSourceSpan(symbol);
            var reason = $"complexity={complexity}, lines={lineCount}";
            if (string.IsNullOrWhiteSpace(filePath))
            {
                reason += ", location=unknown";
            }

            hotspots.Add(new HotspotSummary(
                Label: displayName,
                Path: filePath,
                Reason: reason,
                Score: score,
                SymbolId: metric.SymbolId,
                DisplayName: displayName,
                FilePath: filePath,
                StartLine: startLine,
                EndLine: endLine,
                Complexity: complexity,
                LineCount: lineCount));
        }

        return hotspots
            .OrderByDescending(static h => h.Score)
            .ThenBy(static h => h.SymbolId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol root)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        foreach (var member in root.GetMembers().OrderBy(static m => m.Name, StringComparer.Ordinal))
        {
            stack.Push(member);
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is INamedTypeSymbol namedType)
            {
                yield return namedType;
                foreach (var nested in namedType.GetTypeMembers().OrderByDescending(static m => m.Name, StringComparer.Ordinal))
                {
                    stack.Push(nested);
                }

                continue;
            }

            if (current is INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers().OrderByDescending(static m => m.Name, StringComparer.Ordinal))
                {
                    stack.Push(member);
                }
            }
        }
    }

    private async Task<GetSymbolAtPositionResult> ResolveSymbolAtRequestAsync(
        string? symbolId,
        string? path,
        int? line,
        int? column,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(symbolId))
        {
            var find = await _navigationService.FindSymbolAsync(new FindSymbolRequest(symbolId), ct).ConfigureAwait(false);
            return new GetSymbolAtPositionResult(find.Symbol, find.Error);
        }

        if (!string.IsNullOrWhiteSpace(path) && line.HasValue && column.HasValue)
        {
            return await _navigationService.GetSymbolAtPositionAsync(
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

    private async Task<(INamedTypeSymbol? Symbol, ErrorInfo? Error)> ResolveTypeSymbolAsync(
        ListMembersRequest request,
        Solution solution,
        CancellationToken ct)
    {
        var hasExplicitTypeSymbolId = !string.IsNullOrWhiteSpace(request.TypeSymbolId);
        ISymbol? symbol = null;

        if (hasExplicitTypeSymbolId)
        {
            symbol = await _symbolLookupService.ResolveSymbolAsync(request.TypeSymbolId!, solution, ct).ConfigureAwait(false);
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
        else if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
        {
            symbol = await _symbolLookupService
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

    private static ImmutableArray<ISymbol> CollectMembersWithInheritance(INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        static IEnumerable<INamedTypeSymbol> Traverse(INamedTypeSymbol current)
        {
            yield return current;

            var baseType = current.BaseType;
            while (baseType != null)
            {
                yield return baseType;
                baseType = baseType.BaseType;
            }

            foreach (var iface in current.AllInterfaces.OrderBy(static i => i.ToDisplayString(), StringComparer.Ordinal))
            {
                yield return iface;
            }
        }

        foreach (var declaringType in Traverse(type))
        {
            foreach (var member in declaringType.GetMembers())
            {
                var kind = ToMemberKind(member);
                if (kind == null)
                {
                    continue;
                }

                var key = SymbolIdentity.CreateId(member);
                if (seen.Add(key))
                {
                    builder.Add(member);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static MemberListEntry? ToMemberEntry(
        ISymbol member,
        string? normalizedKind,
        string? normalizedAccessibility,
        string? normalizedBinding)
    {
        var memberKind = ToMemberKind(member);
        if (memberKind == null)
        {
            return null;
        }

        if (normalizedKind != null && !string.Equals(memberKind, normalizedKind, StringComparison.Ordinal))
        {
            return null;
        }

        var accessibility = NormalizeAccessibility(member.DeclaredAccessibility);
        if (normalizedAccessibility != null && !string.Equals(accessibility, normalizedAccessibility, StringComparison.Ordinal))
        {
            return null;
        }

        if (normalizedBinding != null)
        {
            var isStatic = member.IsStatic;
            if ((string.Equals(normalizedBinding, "static", StringComparison.Ordinal) && !isStatic)
                || (string.Equals(normalizedBinding, "instance", StringComparison.Ordinal) && isStatic))
            {
                return null;
            }
        }

        var (filePath, line, column) = GetDeclarationPosition(member);
        return new MemberListEntry(
            member.Kind == SymbolKind.Method && member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                ? constructor.ContainingType.Name
                : member.Name,
            SymbolIdentity.CreateId(member),
            memberKind,
            member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            filePath,
            line,
            column,
            accessibility,
            member.IsStatic);
    }

    public async Task<ListDependenciesResult> ListDependenciesAsync(ListDependenciesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to list dependencies.",
            request.ProjectPath,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ListDependenciesResult(
                Array.Empty<ProjectDependency>(),
                0,
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to list dependencies."));
        }

        var direction = request.Direction?.ToLowerInvariant() switch
        {
            "outgoing" => "outgoing",
            "incoming" => "incoming",
            _ => "both"
        };

        // Select target project
        Project? targetProject = null;
        if (!string.IsNullOrWhiteSpace(request.ProjectPath))
        {
            targetProject = solution.Projects.FirstOrDefault(p => p.FilePath?.EndsWith(request.ProjectPath, StringComparison.OrdinalIgnoreCase) == true);
        }
        else if (!string.IsNullOrWhiteSpace(request.ProjectName))
        {
            targetProject = solution.Projects.FirstOrDefault(p => p.Name.Equals(request.ProjectName, StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(request.ProjectId))
        {
            targetProject = solution.Projects.FirstOrDefault(p => string.Equals(p.Id.Id.ToString(), request.ProjectId, StringComparison.OrdinalIgnoreCase));
        }

        var dependencies = new List<ProjectDependency>();

        if (targetProject != null)
        {
            // Specific project selected
            if (direction == "outgoing" || direction == "both")
            {
                foreach (var reference in targetProject.ProjectReferences)
                {
                    var refProject = solution.Projects.FirstOrDefault(p => p.Id == reference.ProjectId);
                    if (refProject != null)
                    {
                        dependencies.Add(new ProjectDependency(refProject.Name, refProject.Id.Id.ToString()));
                    }
                }
            }

            if (direction == "incoming" || direction == "both")
            {
                foreach (var project in solution.Projects)
                {
                    if (project.ProjectReferences.Any(r => r.ProjectId == targetProject.Id))
                    {
                        dependencies.Add(new ProjectDependency(project.Name, project.Id.Id.ToString()));
                    }
                }
            }
        }
        else
        {
            // No specific project - return all dependencies as a graph
            if (direction == "outgoing" || direction == "both")
            {
                foreach (var project in solution.Projects)
                {
                    foreach (var reference in project.ProjectReferences)
                    {
                        var refProject = solution.Projects.FirstOrDefault(p => p.Id == reference.ProjectId);
                        if (refProject != null)
                        {
                            dependencies.Add(new ProjectDependency(refProject.Name, refProject.Id.Id.ToString()));
                        }
                    }
                }
            }

            if (direction == "incoming")
            {
                // For incoming without a target, show incoming for all projects
                foreach (var project in solution.Projects)
                {
                    var incoming = solution.Projects.Where(p => p.ProjectReferences.Any(r => r.ProjectId == project.Id));
                    foreach (var dep in incoming)
                    {
                        dependencies.Add(new ProjectDependency(dep.Name, dep.Id.Id.ToString()));
                    }
                }
            }
        }

        // Remove duplicates
        var uniqueDeps = dependencies.Distinct().ToList();

        return new ListDependenciesResult(uniqueDeps, uniqueDeps.Count, null);
    }

    private static async Task<ResolveSymbolCandidate[]> ResolveByQualifiedNameAsync(
        string qualifiedName,
        IReadOnlyList<Project> projects,
        CancellationToken ct)
    {
        var normalizedQualifiedName = NormalizeQualifiedName(qualifiedName);
        var shortName = normalizedQualifiedName.Split('.').LastOrDefault();
        if (string.IsNullOrWhiteSpace(shortName))
        {
            return Array.Empty<ResolveSymbolCandidate>();
        }

        var candidates = new List<(ISymbol Symbol, string ProjectName)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in projects.OrderBy(static p => p.Name, StringComparer.Ordinal))
        {
            var symbols = await SymbolFinder.FindDeclarationsAsync(
                    project,
                    shortName,
                    ignoreCase: false,
                    filter: SymbolFilter.TypeAndMember,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            foreach (var symbol in symbols)
            {
                var symbolId = SymbolIdentity.CreateId(symbol);
                var candidateKey = $"{project.Id.Id:N}|{symbolId}";

                if (!seen.Add(candidateKey))
                {
                    continue;
                }

                candidates.Add((symbol, project.Name));
            }
        }

        var strictMatches = candidates
            .Where(match => MatchesQualifiedName(match.Symbol, normalizedQualifiedName))
            .ToArray();
        if (strictMatches.Length > 0)
        {
            return OrderResolveSymbolCandidates(strictMatches, shortName);
        }

        if (!LooksLikeShortNameQuery(normalizedQualifiedName))
        {
            return Array.Empty<ResolveSymbolCandidate>();
        }

        var caseSensitiveMatches = candidates
            .Where(match => string.Equals(match.Symbol.Name, shortName, StringComparison.Ordinal))
            .ToArray();

        var shortNameMatches = caseSensitiveMatches.Length > 0
            ? caseSensitiveMatches
            : candidates
                .Where(match => string.Equals(match.Symbol.Name, shortName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return OrderResolveSymbolCandidates(shortNameMatches, shortName);
    }

    private static ResolveSymbolCandidate[] OrderResolveSymbolCandidates(
        IReadOnlyList<(ISymbol Symbol, string ProjectName)> matches,
        string shortName)
    {
        return matches
            .OrderByDescending(match => string.Equals(match.Symbol.Name, shortName, StringComparison.Ordinal))
            .ThenBy(match => GetResolveSymbolKindPriority(match.Symbol))
            .ThenBy(match => match.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), StringComparer.Ordinal)
            .ThenBy(match => match.ProjectName, StringComparer.Ordinal)
            .ThenBy(match => SymbolIdentity.CreateId(match.Symbol), StringComparer.Ordinal)
            .Select(match =>
            {
                var symbolId = SymbolIdentity.CreateId(match.Symbol);
                var (filePath, line, column) = GetDeclarationPosition(match.Symbol);
                return new ResolveSymbolCandidate(
                    symbolId,
                    match.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    match.Symbol.Kind.ToString(),
                    filePath,
                    line,
                    column,
                    match.ProjectName);
            })
            .ToArray();
    }

    private static int GetResolveSymbolKindPriority(ISymbol symbol)
        => symbol is INamedTypeSymbol ? 0 : 1;

    private static bool LooksLikeShortNameQuery(string normalizedQualifiedName)
        => normalizedQualifiedName.IndexOf('.') < 0;

    private static bool MatchesQualifiedName(ISymbol symbol, string normalizedQualifiedName)
    {
        var full = NormalizeQualifiedName(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        if (string.Equals(full, normalizedQualifiedName, StringComparison.Ordinal))
        {
            return true;
        }

        var csharpError = NormalizeQualifiedName(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        return string.Equals(csharpError, normalizedQualifiedName, StringComparison.Ordinal);
    }

    private static ResolvedSymbolSummary ToResolvedSymbol(ISymbol symbol)
    {
        var (filePath, line, column) = GetDeclarationPosition(symbol);
        return new ResolvedSymbolSummary(
            SymbolIdentity.CreateId(symbol),
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            symbol.Kind.ToString(),
            filePath,
            line,
            column);
    }

    private static (IReadOnlyList<Project> Projects, ErrorInfo? Error) EmptyProjectSelection(Solution solution)
        => (solution.Projects.OrderBy(static p => p.Name, StringComparer.Ordinal).ToArray(), null);

    private static IReadOnlyList<Project> ResolveProjectSelector(
        Solution solution,
        string? projectPath,
        string? projectName,
        string? projectId,
        bool selectorRequired,
        string toolName,
        out ErrorInfo? error)
    {
        var normalizedPath = NormalizeOptional(projectPath);
        var normalizedName = NormalizeOptional(projectName);
        var normalizedId = NormalizeOptional(projectId);

        if (normalizedPath == null && normalizedName == null && normalizedId == null)
        {
            if (selectorRequired)
            {
                error = AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "A project selector is required. Provide projectPath, projectName, or projectId.",
                    $"Call {toolName} with one project selector from load_solution results.",
                    ("field", "project selector"),
                    ("expected", "projectPath|projectName|projectId"));
                return Array.Empty<Project>();
            }

            error = null;
            return solution.Projects.OrderBy(static p => p.Name, StringComparer.Ordinal).ToArray();
        }

        var matches = solution.Projects
            .Where(project => normalizedPath == null || NavigationModelUtilities.MatchesByNormalizedPath(project.FilePath, normalizedPath))
            .Where(project => normalizedName == null || string.Equals(project.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
            .Where(project => normalizedId == null || string.Equals(project.Id.Id.ToString(), normalizedId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
        {
            var provided = string.Join(", ",
                new[]
                {
                    normalizedPath is null ? null : $"projectPath={normalizedPath}",
                    normalizedName is null ? null : $"projectName={normalizedName}",
                    normalizedId is null ? null : $"projectId={normalizedId}"
                }.Where(static value => value != null)!);

            error = AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "Project selector did not match any loaded project.",
                "Use load_solution output to provide an exact projectPath, projectName, or projectId.",
                ("field", "project selector"),
                ("provided", provided));
            return Array.Empty<Project>();
        }

        if (matches.Length > 1)
        {
            var names = string.Join(", ", matches.Select(static project => project.Name));
            error = AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "Project selector is ambiguous and matched multiple projects.",
                "Provide projectPath or projectId to uniquely identify the project.",
                ("field", "project selector"),
                ("matches", names));
            return Array.Empty<Project>();
        }

        error = null;
        return matches;
    }

    private static string? ToTypeKind(INamedTypeSymbol type)
    {
        if (type.IsRecord)
        {
            return "record";
        }

        return type.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            TypeKind.Struct => "struct",
            _ => null
        };
    }

    private static string? ToMemberKind(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "ctor",
            IMethodSymbol method when method.MethodKind == MethodKind.Ordinary || method.MethodKind == MethodKind.UserDefinedOperator
                || method.MethodKind == MethodKind.Conversion || method.MethodKind == MethodKind.ReducedExtension
                || method.MethodKind == MethodKind.DelegateInvoke => "method",
            IPropertySymbol => "property",
            IFieldSymbol field when !field.IsImplicitlyDeclared => "field",
            IEventSymbol => "event",
            _ => null
        };
    }

    private static bool IsPartial(INamedTypeSymbol symbol)
    {
        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax();
            if (syntax is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax typeDeclaration
                && typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }

        return false;
    }

    private static (string FilePath, int? Line, int? Column) GetDeclarationPosition(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static l => l.IsInSource);
        if (location == null)
        {
            return (string.Empty, null, null);
        }

        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        return (span.Path ?? string.Empty, start.Line + 1, start.Character + 1);
    }

    private static (string FilePath, int? StartLine, int? StartColumn, int? EndLine, int? EndColumn) GetSourceSpan(ISymbol? symbol)
    {
        var location = symbol?.Locations.FirstOrDefault(static l => l.IsInSource);
        if (location == null)
        {
            return (string.Empty, null, null, null, null);
        }

        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;
        return (span.Path ?? string.Empty, start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1);
    }

    private static string NormalizeProfile(string? profile)
    {
        var normalized = string.IsNullOrWhiteSpace(profile) ? "standard" : profile.Trim().ToLowerInvariant();
        return normalized is "quick" or "standard" or "deep" ? normalized : "standard";
    }

    private static (int Offset, int Limit) NormalizePaging(int? offset, int? limit)
    {
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var normalizedLimit = limit.HasValue
            ? Math.Clamp(limit.Value, 0, MaximumPageSize)
            : DefaultPageSize;
        return (normalizedOffset, normalizedLimit);
    }

    private static string NormalizeAccessibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedAndInternal => "private_protected",
            Accessibility.ProtectedOrInternal => "protected_internal",
            _ => "not_applicable"
        };
    }

    private static bool TryNormalizeAccessibility(string? accessibility, out string? normalized)
    {
        var value = NormalizeOptional(accessibility);
        if (value == null)
        {
            normalized = null;
            return true;
        }

        normalized = value.Replace('-', '_').ToLowerInvariant();
        if (normalized is "public" or "internal" or "protected" or "private" or "protected_internal" or "private_protected")
        {
            return true;
        }

        normalized = null;
        return false;
    }

    private static bool TryNormalizeTypeKind(string? kind, out string? normalized)
    {
        normalized = NormalizeOptional(kind)?.ToLowerInvariant();
        if (normalized == null)
        {
            return true;
        }

        if (normalized is "class" or "record" or "interface" or "enum" or "struct")
        {
            return true;
        }

        normalized = null;
        return false;
    }

    private static bool TryNormalizeMemberKind(string? kind, out string? normalized)
    {
        normalized = NormalizeOptional(kind)?.ToLowerInvariant();
        if (normalized == null)
        {
            return true;
        }

        if (normalized is "method" or "property" or "field" or "event" or "ctor")
        {
            return true;
        }

        normalized = null;
        return false;
    }

    private static bool TryNormalizeBinding(string? binding, out string? normalized)
    {
        normalized = NormalizeOptional(binding)?.ToLowerInvariant();
        if (normalized == null)
        {
            return true;
        }

        if (normalized is "static" or "instance")
        {
            return true;
        }

        normalized = null;
        return false;
    }

    private static string NormalizeNamespace(INamespaceSymbol? ns)
    {
        if (ns == null || ns.IsGlobalNamespace)
        {
            return string.Empty;
        }

        return ns.ToDisplayString();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ResolveDiscoveryRoot(string? workspaceHintPath)
    {
        var normalizedHint = NormalizeOptional(workspaceHintPath);
        if (normalizedHint == null)
        {
            return Directory.GetCurrentDirectory();
        }

        if (normalizedHint.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || normalizedHint.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(normalizedHint);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Directory.GetCurrentDirectory();
            }

            if (normalizedHint.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(directory);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    return parent;
                }
            }

            return directory;
        }

        return normalizedHint;
    }

    private static string NormalizeQualifiedName(string value)
        => value.Trim().Replace("global::", string.Empty, StringComparison.Ordinal);
}
