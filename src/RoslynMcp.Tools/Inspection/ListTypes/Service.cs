using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Inspection;
using RoslynMcp.Tools.Infrastructure;

namespace RoslynMcp.Tools.Inspection.ListTypes;

public sealed class Service(Infrastructure.Services.Workspace workspace)
{
    private static readonly Context EmptyContext = new(SourceBiases.Unknown, ResultCompletenessStates.Degraded, [], []);

    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return new Result([], 0, EmptyContext, new ErrorInfo(
                "no_solution_loaded",
                "No solution is currently loaded.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nextAction"] = "Call load_solution first to select a solution before listing types."
                }));
        }

        if (!request.Kind.TryNormalizeTypeKind(out var normalizedKind))
            return Invalid("kind", request.Kind ?? string.Empty, "kind must be one of: class, record, interface, enum, or struct.", "class|record|interface|enum|struct");

        if (!request.Accessibility.TryNormalizeAccessibility(out var normalizedAccessibility))
            return Invalid("accessibility", request.Accessibility ?? string.Empty, "accessibility must be one of: public, internal, protected, private, protected_internal, or private_protected.");

        var selectedProjects = ResolveProjectSelector(session.Solution, request.ProjectPath, request.ProjectName, request.ProjectId, out var selectorError);
        if (selectorError is not null)
            return new Result([], 0, EmptyContext, selectorError).WithWorkspaceRelativePaths();

        var entries = new List<Discovery>();
        var generatedFallbackEntries = new List<Discovery>();
        var selectedProjectDocumentPaths = new List<string?>();
        var degradedReasons = new HashSet<string>(StringComparer.Ordinal);
        var limitations = new List<string>();
        var namespacePrefix = request.NamespacePrefix.NormalizeOptional();

        foreach (var project in selectedProjects)
        {
            selectedProjectDocumentPaths.AddRange(project.Documents.Select(static document => document.FilePath));

            var missingDocuments = project.Documents
                .Where(static document => !string.IsNullOrWhiteSpace(document.FilePath) && !File.Exists(document.FilePath!))
                .ToArray();

            if (missingDocuments.Length > 0)
                degradedReasons.Add("missing_artifacts");

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                degradedReasons.Add("compilation_unavailable");
                continue;
            }

            foreach (var type in compilation.Assembly.GlobalNamespace.EnumerateTypes())
            {
                if (!type.Locations.Any(static location => location.IsInSource))
                    continue;

                var kind = type.ToTypeKind();
                if (kind is null)
                    continue;

                if (normalizedKind is not null && !string.Equals(kind, normalizedKind, StringComparison.Ordinal))
                    continue;

                var accessibility = type.DeclaredAccessibility.NormalizeAccessibility();
                if (normalizedAccessibility is not null && !string.Equals(accessibility, normalizedAccessibility, StringComparison.Ordinal))
                    continue;

                var typeNamespace = type.ContainingNamespace.NormalizeNamespace();
                if (namespacePrefix is not null && !typeNamespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
                    continue;

                var (filePath, line, column) = type.GetDeclarationPosition();
                var entry = new Entry(
                    type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    type.ToStableId(),
                    string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue ? null : new SourceLocation(filePath, line.Value, column.Value),
                    kind,
                    type.Arity > 0 ? type.Arity : null);
                var candidate = new Discovery(entry, type);

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
        var returnedEntries = ordered.Skip(offset).Take(limit).Select(candidate => candidate.Enrich(request.IncludeSummary, request.IncludeMembers)).ToArray();

        var selectedVisibility = selectedProjectDocumentPaths.AssessPaths();
        var returnedSourceBias = ordered.Length > 0
            ? ordered.Select(static entry => entry.Entry.Location?.FilePath ?? string.Empty).AssessPaths().Visibility
            : selectedVisibility.Visibility;

        if (ordered.Length == 0 && degradedReasons.Contains("missing_artifacts"))
            limitations.Add("Type discovery is degraded because referenced source or generated artifacts are missing from the current workspace.");

        if (ordered.Length == 0 && degradedReasons.Contains("compilation_unavailable"))
            limitations.Add("Type discovery is degraded because the selected project compilation is not available yet.");

        var recommendedNextStep = degradedReasons.Count > 0
            ? "Run dotnet restore/build and retry list_types if the current project should expose additional declarations."
            : null;

        var context = new Context(
            returnedSourceBias,
            DetermineCompleteness(ordered.Length, degradedReasons.Count > 0, selectedVisibility, generatedFallbackEntries.Count > 0),
            [.. limitations.Distinct(StringComparer.Ordinal)],
            [.. degradedReasons.OrderBy(static value => value, StringComparer.Ordinal)],
            recommendedNextStep);

        return new Result(returnedEntries, ordered.Length, context).WithWorkspaceRelativePaths();
    }

    private static IReadOnlyList<Project> ResolveProjectSelector(Solution solution, string? projectPath, string? projectName, string? projectId, out ErrorInfo? error)
    {
        var matches = solution.Resolve(projectPath, projectName, projectId, selectorRequired: true, toolName: "list_types", out var selectorError);
        error = selectorError is null ? null : new ErrorInfo(selectorError.Code, selectorError.Message, selectorError.Details);
        return matches;
    }

    private static Result Invalid(string field, string provided, string message, string? expected = null)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["field"] = field,
            ["provided"] = provided
        };

        if (!string.IsNullOrWhiteSpace(expected))
            details["expected"] = expected;

        return new Result([], 0, EmptyContext, new ErrorInfo("invalid_input", message, details));
    }

    private static string DetermineCompleteness(int totalCount, bool isDegraded, VisibilityAssessment selectedVisibility, bool hadGeneratedFallback)
    {
        if (isDegraded)
            return ResultCompletenessStates.Degraded;

        if (totalCount > 0 && hadGeneratedFallback && !selectedVisibility.HasHandwritten)
            return ResultCompletenessStates.Partial;

        if (totalCount == 0 && hadGeneratedFallback && selectedVisibility.HasGenerated && !selectedVisibility.HasHandwritten)
            return ResultCompletenessStates.Partial;

        return ResultCompletenessStates.Complete;
    }
}
