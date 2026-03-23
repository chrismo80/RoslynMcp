using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Inspection.UnderstandProjects.Builders;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.UnderstandProjects;

[McpServerToolType]
public sealed class Tool(WorkspaceManager workspaceManager, SolutionManager solutionManager)
{
    [McpServerTool(Name = "understand_projects", Title = "Understand Projects", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need a quick overview of the loaded solution's project landscape. It returns real project relationships with projectPath lists, compact per-project type summaries for standard/deep profiles, and hotspots only for deep analysis.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("Analysis depth. quick omits types and hotspots, standard includes types, deep includes types and 10 hotspots. Defaults to standard.")]
        string? profile = null)
    {
        if (solutionManager.Solution is null)
        {
            return new Result([], new ErrorInfo("No solution is currently loaded.", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nextAction"] = "Call load_solution first to select a solution before understanding projects."
            }));
        }

        var projects = await ProjectSummaryBuilder.BuildAsync(solutionManager.Solution, profile is Extensions.Profiles.Deep, cancellationToken)
            .ConfigureAwait(false);

        return new Result(projects);
    }
}
