using Microsoft.Extensions.DependencyInjection;
using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Infrastructure;

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
        public ProjectSummary WithWorkspaceRelativePaths(string workspaceRoot)
            => project with { Path = project.Path?.ToWorkspaceRelativePathIfPossible(workspaceRoot) };
    }

    extension(ErrorInfo? error)
    {
        public ErrorInfo? WithWorkspaceRelativePaths(string workspaceRoot)
        {
            if (error?.Details is null || error.Details.Count == 0)
                return error;

            Dictionary<string, string>? updatedDetails = null;
            foreach (var pair in error.Details)
            {
                if (!ShouldRewritePathDetail(pair.Key, error.Details))
                    continue;

                var outwardPath = pair.Value.ToWorkspaceRelativePathIfPossible(workspaceRoot);
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
        public Result WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                SelectedSolutionPath = result.SelectedSolutionPath?.ToWorkspaceRelativePathIfPossible(workspaceRoot),
                WorkspaceId = result.WorkspaceId.ToWorkspaceRelativePathIfPossible(workspaceRoot),
                Projects = [.. result.Projects.Select(project => project.WithWorkspaceRelativePaths(workspaceRoot))],
                Error = result.Error.WithWorkspaceRelativePaths(workspaceRoot)
            };
    }

    extension(IReadOnlyList<Diagnostic> diagnostics)
    {
        public DiagnosticsSummary ToDiagnosticsSummary()
        {
            var filtered = diagnostics
                .Where(static diagnostic => SourceVisibility.ShouldIncludeInHumanResults(diagnostic.Location.GetLineSpan().Path))
                .ToArray();

            return new DiagnosticsSummary(
                filtered.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                filtered.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
                filtered.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Info || diagnostic.Severity == DiagnosticSeverity.Hidden),
                filtered.Length);
        }
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

        return (details.TryGetValue("field", out var field) && PathFieldNames.Contains(field))
            || (details.TryGetValue("parameter", out var parameter) && PathFieldNames.Contains(parameter));
    }

}
