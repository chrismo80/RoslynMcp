using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Inspection;

internal static class ProjectSelector
{
    internal static IReadOnlyList<Project> Resolve(
        this Solution solution,
        string? projectPath,
        string? projectName,
        string? projectId,
        bool selectorRequired,
        string toolName,
        out ProjectSelectorError? error)
    {
        var normalizedPath = projectPath.NormalizeOptional();
        var normalizedName = projectName.NormalizeOptional();
        var normalizedId = projectId.NormalizeOptional();

        if (normalizedPath is null && normalizedName is null && normalizedId is null)
        {
            if (!selectorRequired)
            {
                error = null;
                return [.. solution.Projects.OrderBy(static project => project.Name, StringComparer.Ordinal)];
            }

            error = new ProjectSelectorError(
                "invalid_input",
                "A project selector is required. Provide projectPath, projectName, or projectId.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["field"] = "project selector",
                    ["expected"] = "projectPath|projectName|projectId",
                    ["nextAction"] = $"Call {toolName} with one project selector from load_solution results."
                });
            return [];
        }

        var matches = solution.Projects
            .Where(project => (normalizedPath is null || project.FilePath.MatchesByNormalizedPath(normalizedPath) || project.FilePath?.ToWorkspaceRelativePathIfPossible().MatchesByNormalizedPath(normalizedPath) == true) && (normalizedName is null || string.Equals(project.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            .Where(project => normalizedId is null || string.Equals(project.Id.Id.ToString(), normalizedId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
        {
            error = new ProjectSelectorError(
                "invalid_input",
                normalizedId is null ? "Project selector did not match any loaded project." : "projectId did not match any project in the active workspace snapshot.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["field"] = "project selector",
                    ["provided"] = string.Join(", ", new[]
                    {
                        normalizedPath is null ? null : $"projectPath={normalizedPath}",
                        normalizedName is null ? null : $"projectName={normalizedName}",
                        normalizedId is null ? null : $"projectId={normalizedId}"
                    }.Where(static value => value is not null)!),
                    ["nextAction"] = normalizedId is null
                        ? "Use load_solution output to provide an exact projectPath, projectName, or projectId."
                        : "projectId values are snapshot-local and can change after reload. Refresh selectors from the current snapshot or prefer projectPath for automation."
                });
            return [];
        }

        if (matches.Length > 1)
        {
            error = new ProjectSelectorError(
                "invalid_input",
                "Project selector is ambiguous and matched multiple projects.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["field"] = "project selector",
                    ["matches"] = string.Join(", ", matches.Select(static project => project.Name)),
                    ["nextAction"] = "Provide projectPath or projectId to uniquely identify the project."
                });
            return [];
        }

        error = null;
        return matches;
    }
}

internal sealed record ProjectSelectorError(string Code, string Message, IReadOnlyDictionary<string, string>? Details = null);
