using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class ListDependenciesTools
{
    private readonly ICodeUnderstandingService _codeUnderstandingService;

    public ListDependenciesTools(ICodeUnderstandingService codeUnderstandingService)
    {
        _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));
    }

    [McpServerTool(Name = "list_dependencies", Title = "List Dependencies", ReadOnly = true, Idempotent = true)]
    [Description("Lists project dependencies. Returns all projects that a project depends on (outgoing), or projects that depend on it (incoming), or both. Use at session start to understand project relationships. Without a specific project, returns all outgoing dependencies across the solution.")]
    public Task<ListDependenciesResult> ListDependenciesAsync(
        CancellationToken cancellationToken,
        [Description("Project selector option 1: exact project path from load_solution output. Provide exactly one selector (path, name, or id).")]
        string? projectPath = null,
        [Description("Project selector option 2: project name from load_solution output.")]
        string? projectName = null,
        [Description("Project selector option 3: projectId from load_solution output.")]
        string? projectId = null,
        [Description("Dependency direction: 'outgoing' (projects this project depends on), 'incoming' (projects that depend on this project), or 'both'. Defaults to 'both'.")]
        string? direction = null)
        => _codeUnderstandingService.ListDependenciesAsync(
            ToolContractMapper.ToListDependenciesRequest(projectPath, projectName, projectId, direction),
            cancellationToken);
}
