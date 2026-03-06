using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class TraceCallFlowTool(IFlowTraceService flowTraceService) : Tool
{
    private readonly IFlowTraceService _flowTraceService = flowTraceService ?? throw new ArgumentNullException(nameof(flowTraceService));

    [McpServerTool(Name = "trace_call_flow", Title = "Trace Call Flow", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to understand how code flows through your system — either finding what calls a specific symbol (upstream) or what a symbol calls (downstream). This is essential for debugging, impact analysis, and understanding architectural patterns.")]
    public Task<TraceFlowResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("The stable symbol ID, obtained from resolve_symbol, list_types, or list_members. Provide this OR path+line+column.")]
        string? symbolId = null,
        [Description("Path to a source file. Provide this together with line and column instead of symbolId.")]
        string? path = null,
        [Description("Line number (1-based) pointing to the symbol in the source file.")]
        int? line = null,
        [Description("Column number (1-based) pointing to the symbol in the source file.")]
        int? column = null,
        [Description("Which direction to trace. upstream finds callers (who uses this). downstream finds callees (what this calls). both returns both directions. Defaults to both.")]
        string? direction = null,
        [Description("How many levels of the call chain to traverse. Defaults to 2. Use larger values for deeper analysis. Null is treated the same as omitting the parameter.")]
        int? depth = null
        )
        => _flowTraceService.TraceFlowAsync(symbolId.ToTraceFlowRequest(path, line, column, direction, depth), cancellationToken);
}
