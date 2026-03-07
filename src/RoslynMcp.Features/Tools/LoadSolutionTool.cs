using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class LoadSolutionTool(IWorkspaceBootstrapService workspaceBootstrapService) : Tool
{
    private readonly IWorkspaceBootstrapService _workspaceBootstrapService = workspaceBootstrapService ?? throw new ArgumentNullException(nameof(workspaceBootstrapService));

    [McpServerTool(Name = "load_solution", Title = "Load Solution", ReadOnly = false, Idempotent = false)]
    [Description("Use this tool when you need to start working with a .NET solution and no solution has been loaded yet. This must be the first tool called in a session before any code analysis or navigation tools can be used. In fresh or detached worktrees, baseline diagnostics can be inflated when generated/intermediate artifacts have not been restored yet.")]
    public Task<LoadSolutionResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("(optional): Absolute path to a `.sln` file, or to a directory used as the recursive discovery root for `.sln`/`.slnx` files. If omitted, the tool will auto-detect from the current workspace.")]
        string? solutionHintPath = null
        )
        => _workspaceBootstrapService.LoadSolutionAsync(solutionHintPath.ToLoadSolutionRequest(), cancellationToken);
}
