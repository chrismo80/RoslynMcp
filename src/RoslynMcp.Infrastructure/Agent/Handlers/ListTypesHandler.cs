using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using RoslynMcp.Infrastructure.Documentation;
using RoslynMcp.Infrastructure.Navigation;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

/// <summary>
/// Lists types (class, record, interface, enum, struct) in the solution with optional filtering by accessibility.
/// Returns type metadata including namespace, accessibility, and XML documentation summary.
/// </summary>
internal sealed class ListTypesHandler(
    CodeUnderstandingQueryService queries,
    ISymbolDocumentationProvider symbolDocumentationProvider)
{
    private static readonly ResultContextMetadata EmptyContext = new(SourceBiases.Unknown, ResultCompletenessStates.Degraded, [], []);

    public async Task<ListTypesResult> HandleAsync(ListTypesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before listing types.",
            request.ProjectPath,
            ct).ConfigureAwait(false);

        if (solution == null)
            return new ListTypesResult([], 0, EmptyContext, AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before listing types."));

        if (!request.Kind.TryNormalizeTypeKind(out var normalizedKind))
        {
            return new ListTypesResult([], 0, EmptyContext,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "kind must be one of: class, record, interface, enum, struct.",
                    "Retry list_types with a supported kind filter or omit kind.",
                    ("field", "kind"),
                    ("provided", request.Kind ?? string.Empty),
                    ("expected", "class|record|interface|enum|struct")));
        }

        if (!request.Accessibility.TryNormalizeAccessibility(out var normalizedAccessibility))
        {
            return new ListTypesResult([], 0, EmptyContext,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "accessibility must be one of: public, internal, protected, private, protected_internal, private_protected.",
                    "Retry list_types with a supported accessibility filter or omit accessibility.",
                    ("field", "accessibility"),
                    ("provided", request.Accessibility ?? string.Empty)));
        }

        var selectedProjects = solution.ResolveProjectSelector(
            request.ProjectPath,
            request.ProjectName,
            request.ProjectId,
            selectorRequired: true,
            toolName: "list_types",
            out var selectorError);

        if (selectorError != null)
            return new ListTypesResult([], 0, EmptyContext, selectorError);

        var namespacePrefix = request.NamespacePrefix.NormalizeOptional();
        var entries = new List<TypeDiscoveryEntry>();
        var generatedFallbackEntries = new List<TypeDiscoveryEntry>();
        var selectedProjectDocumentPaths = new List<string?>();
        var degradedReasons = new HashSet<string>(StringComparer.Ordinal);
        var limitations = new List<string>();

        foreach (var project in selectedProjects)
        {
            selectedProjectDocumentPaths.AddRange(project.Documents.Select(static document => document.FilePath));

            var missingDocuments = project.Documents
                .Where(static document => !string.IsNullOrWhiteSpace(document.FilePath))
                .Where(static document => !File.Exists(document.FilePath!))
                .ToArray();

            if (missingDocuments.Length > 0)
            {
                degradedReasons.Add("missing_artifacts");
            }

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                degradedReasons.Add("compilation_unavailable");
                continue;
            }

            foreach (var type in compilation.Assembly.GlobalNamespace.EnumerateTypes())
            {
                if (!type.Locations.Any(static location => location.IsInSource))
                    continue;

                var kind = type.ToTypeKind();
                if (kind == null)
                    continue;

                if (normalizedKind != null && !string.Equals(kind, normalizedKind, StringComparison.Ordinal))
                    continue;

                var accessibility = type.DeclaredAccessibility.NormalizeAccessibility();
                if (normalizedAccessibility != null && !string.Equals(accessibility, normalizedAccessibility, StringComparison.Ordinal))
                    continue;

                var typeNamespace = type.ContainingNamespace.NormalizeNamespace();
                if (namespacePrefix != null && !typeNamespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
                    continue;

                var (filePath, line, column) = type.GetDeclarationPosition();
                var entry = new TypeListEntry(
                    type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    type.CreateId().ToExternal(),
                    CreateOptionalSourceLocation(filePath, line, column),
                    kind,
                    type.IsPartial(),
                    type.Arity > 0 ? type.Arity : null,
                    null);
                var candidate = new TypeDiscoveryEntry(entry, type);

                if (!SourceVisibility.ShouldIncludeInHumanResults(filePath))
                {
                    generatedFallbackEntries.Add(candidate);
                    continue;
                }

                entries.Add(candidate);
            }
        }

        if (entries.Count == 0 && generatedFallbackEntries.Count > 0)
        {
            entries.AddRange(generatedFallbackEntries);
            limitations.Add("Only generated declarations are currently visible for the selected project selector.");
        }
        else if (generatedFallbackEntries.Count > 0)
        {
            limitations.Add("Default results prefer handwritten declarations; generated declarations were omitted from the visible list.");
        }

        var ordered = entries
            .OrderBy(static item => item.Entry.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.Entry.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Entry.Arity ?? 0)
            .ThenBy(static item => item.Entry.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var (offset, limit) = request.Offset.NormalizePaging(request.Limit);
        var paged = ordered.Skip(offset).Take(limit).ToArray();
        var returnedEntries = paged
            .Select(candidate => EnrichEntry(candidate, request.IncludeSummary, request.IncludeMembers, symbolDocumentationProvider))
            .ToArray();

        var selectedVisibility = SourceVisibility.AssessPaths(selectedProjectDocumentPaths);
        var returnedSourceBias = ordered.Length > 0
            ? SourceVisibility.DetermineResultSourceBias(ordered.Select(static entry => entry.Entry.Location?.FilePath ?? string.Empty))
            : selectedVisibility.Visibility;

        if (ordered.Length == 0 && degradedReasons.Contains("missing_artifacts"))
            limitations.Add("Type discovery is degraded because referenced source or generated artifacts are missing from the current workspace.");

        if (ordered.Length == 0 && degradedReasons.Contains("compilation_unavailable"))
        {
            limitations.Add("Type discovery is degraded because the selected project compilation is not available yet.");
        }

        var recommendedNextStep = degradedReasons.Count > 0
            ? "Run dotnet restore/build and retry list_types if the current project should expose additional declarations."
            : null;

        var context = new ResultContextMetadata(
            returnedSourceBias,
            DetermineCompleteness(ordered.Length, degradedReasons.Count > 0, selectedVisibility, generatedFallbackEntries.Count > 0),
            limitations.Distinct(StringComparer.Ordinal).ToArray(),
            degradedReasons.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            recommendedNextStep);

        return new ListTypesResult(returnedEntries, ordered.Length, context);
    }

    private static TypeListEntry EnrichEntry(
        TypeDiscoveryEntry candidate,
        bool includeSummary,
        bool includeMembers,
        ISymbolDocumentationProvider symbolDocumentationProvider)
    {
        var summary = includeSummary
            ? symbolDocumentationProvider.GetDocumentation(candidate.Symbol)?.Summary
            : candidate.Entry.Summary;
        var members = includeMembers
            ? GetDeclaredLightweightMembers(candidate.Symbol)
            : candidate.Entry.Members;

        return candidate.Entry with
        {
            Summary = summary,
            Members = members
        };
    }

    private static IReadOnlyList<string> GetDeclaredLightweightMembers(INamedTypeSymbol type)
    {
        return type.GetMembers()
            .Where(member => member.DeclaredAccessibility > Accessibility.Private)
            .Select(member =>
            {
                var (filePath, _, _) = member.GetDeclarationPosition();
                return new
                {
                    Member = member,
                    Kind = member.ToMemberKind(),
                    Entry = member.ToLightweightMemberEntry(),
                    DisplayName = member.Kind == SymbolKind.Method && member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                        ? constructor.ContainingType.Name
                        : member.Name,
                    FilePath = filePath,
                    Signature = member.ToLightweightMemberSignature()
                };
            })
            .Where(static item => item.Kind != null)
            .Where(static item => SourceVisibility.ShouldIncludeInHumanResults(item.FilePath))
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Signature, StringComparer.Ordinal)
            .ThenBy(static item => item.Member.CreateId(), StringComparer.Ordinal)
            .Select(static item => item.Entry!)
            .ToArray();
    }

    private static string DetermineCompleteness(
        int totalCount,
        bool isDegraded,
        SourceVisibilityAssessment selectedVisibility,
        bool hadGeneratedFallback)
    {
        if (isDegraded)
            return ResultCompletenessStates.Degraded;

        if (totalCount > 0 && hadGeneratedFallback && !selectedVisibility.HasHandwritten)
            return ResultCompletenessStates.Partial;

        if (totalCount == 0 && hadGeneratedFallback && selectedVisibility.HasGenerated && !selectedVisibility.HasHandwritten)
            return ResultCompletenessStates.Partial;

        return ResultCompletenessStates.Complete;
    }

    private sealed record TypeDiscoveryEntry(TypeListEntry Entry, INamedTypeSymbol Symbol);

    private static SourceLocation? CreateOptionalSourceLocation(string filePath, int? line, int? column)
        => string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue
            ? null
            : new SourceLocation(filePath, line.Value, column.Value);
}
