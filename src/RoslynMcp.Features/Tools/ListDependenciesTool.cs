using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class ListDependenciesTool(ICodeUnderstandingService codeUnderstandingService) : Tool
{
    private readonly ICodeUnderstandingService _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));

    [McpServerTool(Name = "list_dependencies", Title = "List Dependencies", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to understand how projects relate to each other within a solution. For automation, prefer projectPath as the stable selector; projectId is snapshot-local to the current loaded workspace.")]
    public Task<ListDependenciesResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("Exact path to a project file (.csproj). Specify only one of projectPath, projectName, or projectId.")]
        string? projectPath = null,
        [Description("Name of a project. Specify only one of projectPath, projectName, or projectId.")]
        string? projectName = null,
        [Description("Project identifier from the current loaded workspace snapshot. projectId values are snapshot-local and can change after reload, so prefer projectPath for durable automation. Specify only one of projectPath, projectName, or projectId.")]
        string? projectId = null,
        [Description("Which direction of dependencies to return. outgoing shows what the selected project depends on. incoming shows what depends on the selected project. both returns both directions. Defaults to both.")]
        string? direction = null
        )
        => _codeUnderstandingService.ListDependenciesAsync(projectPath.ToListDependenciesRequest(projectName, projectId, direction), cancellationToken);
}
