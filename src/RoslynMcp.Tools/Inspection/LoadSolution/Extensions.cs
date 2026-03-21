using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Tools.Inspection.LoadSolution;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddLoadSolutionTool() => services
            .AddSingleton<Service>()
            .AddSingleton<Tool>();
    }

    extension(string? solutionHintPath)
    {
        public Request ToRequest() => new(solutionHintPath?.NormalizeOptional());

        public string ToWorkspaceAbsolutePath(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(solutionHintPath))
                return solutionHintPath!;

            var trimmedPath = solutionHintPath.Trim();
            try
            {
                return Path.IsPathRooted(trimmedPath)
                    ? Path.GetFullPath(trimmedPath)
                    : Path.GetFullPath(trimmedPath, workspaceRoot);
            }
            catch
            {
                return trimmedPath;
            }
        }

        public string ToWorkspaceRelativePathIfPossible(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(solutionHintPath))
                return solutionHintPath!;

            var absolutePath = solutionHintPath.ToWorkspaceAbsolutePath(workspaceRoot);
            if (!Path.IsPathRooted(absolutePath))
                return absolutePath;

            try
            {
                var normalizedWorkspaceRoot = workspaceRoot.EnsureTrailingDirectorySeparator();
                var normalizedAbsolutePath = Path.GetFullPath(absolutePath);
                if (!normalizedAbsolutePath.StartsWith(normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
                    return normalizedAbsolutePath;

                return Path.GetRelativePath(workspaceRoot, normalizedAbsolutePath);
            }
            catch
            {
                return absolutePath;
            }
        }
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
                Projects = result.Projects.Select(project => project.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.WithWorkspaceRelativePaths(workspaceRoot)
            };
    }

    extension(IReadOnlyList<DiagnosticItem> diagnostics)
    {
        public DiagnosticsSummary ToDiagnosticsSummary() => new(
            diagnostics.Count(static diagnostic => string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            diagnostics.Count(static diagnostic => string.Equals(diagnostic.Severity, "warning", StringComparison.OrdinalIgnoreCase)),
            diagnostics.Count(static diagnostic => string.Equals(diagnostic.Severity, "info", StringComparison.OrdinalIgnoreCase)),
            diagnostics.Count);
    }

    extension(RoslynMcp.Core.Models.ErrorInfo? error)
    {
        public ErrorInfo? ToLocalError(string? nextAction = null)
        {
            if (error is null)
                return null;

            if (string.IsNullOrWhiteSpace(nextAction))
                return new ErrorInfo(error.Code, error.Message, error.Details);

            if (error.Details is not null && error.Details.TryGetValue("nextAction", out var existing) && !string.IsNullOrWhiteSpace(existing))
                return new ErrorInfo(error.Code, error.Message, error.Details);

            var details = new Dictionary<string, string>(StringComparer.Ordinal);
            if (error.Details is not null)
            {
                foreach (var pair in error.Details)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                        details[pair.Key] = pair.Value;
                }
            }

            details["nextAction"] = nextAction;
            return new ErrorInfo(error.Code, error.Message, details);
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

        return details.TryGetValue("field", out var field) && PathFieldNames.Contains(field)
            || details.TryGetValue("parameter", out var parameter) && PathFieldNames.Contains(parameter);
    }

    private static string EnsureTrailingDirectorySeparator(this string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}