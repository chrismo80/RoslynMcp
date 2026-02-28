using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class CodeSmellTools
{
    private readonly ICodeSmellFindingService _codeSmellFindingService;

    public CodeSmellTools(ICodeSmellFindingService codeSmellFindingService)
    {
        _codeSmellFindingService = codeSmellFindingService ?? throw new ArgumentNullException(nameof(codeSmellFindingService));
    }

    [McpServerTool(Name = "find_codesmells", Title = "Find Code Smells", ReadOnly = true, Idempotent = true)]
    [Description("Finds deterministic code-smell candidates in a document by probing Roslyn diagnostics and refactoring anchors.")]
    public Task<FindCodeSmellsResult> FindCodeSmellsAsync(
        [Description("Required source document path. The file must exist in the currently loaded solution.")]
        string path,
        CancellationToken cancellationToken)
        => _codeSmellFindingService.FindCodeSmellsAsync(
            ToolContractMapper.ToFindCodeSmellsRequest(path),
            cancellationToken);
}