using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class ExplainSymbolTools
{
    private readonly ICodeUnderstandingService _codeUnderstandingService;

    public ExplainSymbolTools(ICodeUnderstandingService codeUnderstandingService)
    {
        _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));
    }

    [McpServerTool(Name = "explain_symbol", Title = "Explain Symbol", ReadOnly = true, Idempotent = true)]
    [Description("Explains a resolved symbol: its role, signature, containing namespace/type, key references (where it's used), and impact hints (zones with high reference density). Requires symbolId from resolve_symbol OR path+line+column pointing to the symbol.")]
    public Task<ExplainSymbolResult> ExplainSymbolAsync(
        CancellationToken cancellationToken,
        [Description("Symbol selector mode A: canonical symbolId. Use this, or provide path+line+column.")]
        string? symbolId = null,
        [Description("Symbol selector mode B: source file path used with line+column.")]
        string? path = null,
        [Description("Symbol selector mode B: 1-based line number used with path+column.")]
        int? line = null,
        [Description("Symbol selector mode B: 1-based column number used with path+line.")]
        int? column = null)
        => _codeUnderstandingService.ExplainSymbolAsync(
            ToolContractMapper.ToExplainSymbolRequest(symbolId, path, line, column),
            cancellationToken);
}
