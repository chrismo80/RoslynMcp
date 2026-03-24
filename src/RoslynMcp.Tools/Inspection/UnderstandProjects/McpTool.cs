using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.UnderstandProjects;

[McpServerToolType]
public sealed class McpTool(
    SolutionManager solutionManager,
    WorkspaceManager workspaceManager,
    SymbolManager symbolManager)
    : Tool
{
    [McpServerTool(Name = "understand_projects", Title = "Understand Projects", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need a quick overview of the loaded solution's project landscape. It returns real project relationships with projectPath lists, compact per-project type summaries for standard/deep profiles, and hotspots only for deep analysis.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("Analysis depth. standard only project references, deep includes types. Defaults to standard.")]
        string? profile = null)
    {
        if (solutionManager.Solution is null)
        {
            return new Result([], new ErrorInfo("No solution is currently loaded.", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nextAction"] = "Call load_solution first to select a solution before understanding projects."
            }));
        }

        var projects = await BuildAsync(solutionManager.Solution, profile is "deep", cancellationToken)
            .ConfigureAwait(false);

        return new Result(projects);
    }

    private async Task<IReadOnlyList<ProjectSummary>> BuildAsync(Solution solution, bool includeTypes, CancellationToken cancellationToken)
    {
        var outgoingByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var incomingByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            var projectPath = project.FilePath ?? string.Empty;

            outgoingByPath.TryAdd(projectPath, []);
            incomingByPath.TryAdd(projectPath, []);
        }

        foreach (var project in solution.Projects)
        {
            var sourcePath = project.FilePath ?? string.Empty;

            foreach (var reference in project.ProjectReferences)
            {
                var dependency = solution.GetProject(reference.ProjectId);

                if (dependency?.FilePath is null)
                    continue;

                outgoingByPath[sourcePath].Add(dependency.FilePath);
                incomingByPath[dependency.FilePath].Add(sourcePath);
            }
        }

        var summaries = new List<ProjectSummary>();

        foreach (var project in solution.Projects)
        {
            var projectPath = project.FilePath ?? string.Empty;
            var types = includeTypes ? await BuildProjectTypesAsync(project, cancellationToken).ConfigureAwait(false) : [];

            summaries.Add(new ProjectSummary(
                project.Name,
                workspaceManager.ToRelativePathIfPossible(project?.FilePath ?? string.Empty),
                [.. outgoingByPath[projectPath].Select(workspaceManager.ToRelativePathIfPossible).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)],
                [.. incomingByPath[projectPath].Select(workspaceManager.ToRelativePathIfPossible).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)],
                types));
        }

        return [.. summaries
            .OrderByDescending(static project => project.OutgoingDependencyProjectPaths.Count + project.IncomingDependencyProjectPaths.Count)
            .ThenBy(static project => project.Name, StringComparer.Ordinal)];
    }

    private async Task<IReadOnlyList<string>> BuildProjectTypesAsync(Project project, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        if (compilation is null)
            return [];

        return compilation!.GlobalNamespace.GetTypes()
            .Select(symbol => TypeSymbol.From(symbol, symbolManager))
            .Select(type => type.ToLine())
            .ToList();
    }
}