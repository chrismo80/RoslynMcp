using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.UnderstandProjects;

[McpServerToolType]
public sealed class Tool(Service service)
{
    [McpServerTool(Name = "understand_projects", Title = "Understand Projects", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need a quick overview of the loaded solution's project landscape. It returns real project relationships with projectPath lists, compact per-project type summaries for standard/deep profiles, and hotspots only for deep analysis.")]
    public Task<Result> Run(CancellationToken cancellationToken,
        [Description("Analysis depth. quick omits types and hotspots, standard includes types, deep includes types and 10 hotspots. Defaults to standard.")]
        string? profile = null)
        => service.RunAsync(profile.ToRequest(), cancellationToken);
}
