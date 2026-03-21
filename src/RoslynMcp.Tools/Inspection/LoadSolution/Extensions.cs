using Microsoft.Extensions.DependencyInjection;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Inspection.LoadSolution;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddLoadSolutionTool() => services
            .AddSingleton<Service>()
            .AddSingleton<Tool>();
    }

    internal static bool IsExplicitSolutionPath(this string? path) => !string.IsNullOrWhiteSpace(path) &&
        (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));

    extension(string? solutionHintPath)
    {
        public Request ToRequest() => new(solutionHintPath.NormalizeOptional());
    }

    extension(ProjectSummary project)
    {
        public ProjectSummary WithWorkspaceRelativePaths()
            => project with { Path = project.Path?.ToWorkspaceRelativePathIfPossible() };
    }

    extension(ErrorInfo? error)
    {
        public ErrorInfo? WithWorkspaceRelativePaths()
        {
            if (error?.Details is null || error.Details.Count == 0)
                return error;

            Dictionary<string, string>? updatedDetails = null;
            foreach (var pair in error.Details)
            {
                if (!ShouldRewritePathDetail(pair.Key, error.Details))
                    continue;

                var outwardPath = pair.Value.ToWorkspaceRelativePathIfPossible();
                if (string.Equals(outwardPath, pair.Value, StringComparison.Ordinal))
                    continue;

                updatedDetails ??= new Dictionary<string, string>(error.Details, StringComparer.Ordinal);
                updatedDetails[pair.Key] = outwardPath;
            }

            return updatedDetails is null ? error : error with { Details = updatedDetails };
        }
    }

    extension(Result result)
    {
        public Result WithWorkspaceRelativePaths()
            => result with
            {
                SelectedSolutionPath = result.SelectedSolutionPath?.ToWorkspaceRelativePathIfPossible(),
                WorkspaceId = result.WorkspaceId.ToWorkspaceRelativePathIfPossible(),
                Projects = result.Projects.Select(project => project.WithWorkspaceRelativePaths()).ToArray(),
                Error = result.Error.WithWorkspaceRelativePaths()
            };
    }

    extension(IReadOnlyList<Diagnostic> diagnostics)
    {
        public DiagnosticsSummary ToDiagnosticsSummary() => new(
            diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
            diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Info || diagnostic.Severity == DiagnosticSeverity.Hidden),
            diagnostics.Count);
    }

    private static readonly HashSet<string> PathDetailKeys =
    [
        "path",
        "file",
        "filepath",
        "projectpath",
        "solutionpath",
        "selectedsolutionpath",
        "workspaceroot",
        "target",
        "targetpath"
    ];

    private static readonly HashSet<string> PathFieldNames =
    [
        "path",
        "filepath",
        "projectpath",
        "solutionhintpath",
        "solutionpath",
        "selectedsolutionpath",
        "target",
        "workspaceroot"
    ];

    private static bool ShouldRewritePathDetail(string key, IReadOnlyDictionary<string, string> details)
    {
        if (PathDetailKeys.Contains(key))
            return true;

        if (!string.Equals(key, "provided", StringComparison.OrdinalIgnoreCase))
            return false;

        return details.TryGetValue("field", out var field) && PathFieldNames.Contains(field)
            || details.TryGetValue("parameter", out var parameter) && PathFieldNames.Contains(parameter);
    }

}