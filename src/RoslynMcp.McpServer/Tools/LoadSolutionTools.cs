using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class LoadSolutionTools
{
    private readonly IWorkspaceBootstrapService _workspaceBootstrapService;

    public LoadSolutionTools(IWorkspaceBootstrapService workspaceBootstrapService)
    {
        _workspaceBootstrapService = workspaceBootstrapService ?? throw new ArgumentNullException(nameof(workspaceBootstrapService));
    }

    [McpServerTool(Name = "load_solution", Title = "Load Solution", ReadOnly = false, Idempotent = false)]
    [Description("MUST be called first: loads a .sln file and initializes the Roslyn workspace. All other tools require a loaded solution to work. Optionally accepts a solution path (absolute or workspace-relative). Returns project list and baseline diagnostics.")]
    public Task<LoadSolutionResult> LoadSolutionAsync(
        CancellationToken cancellationToken,
        [Description("Optional solution hint path (absolute or workspace-relative).")]
        string? solutionHintPath = null)
        => _workspaceBootstrapService.LoadSolutionAsync(ToolContractMapper.ToLoadSolutionRequest(solutionHintPath), cancellationToken);
}