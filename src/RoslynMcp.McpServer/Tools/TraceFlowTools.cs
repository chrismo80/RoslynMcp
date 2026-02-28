using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class TraceFlowTools
{
    private readonly IFlowTraceService _flowTraceService;

    public TraceFlowTools(IFlowTraceService flowTraceService)
    {
        _flowTraceService = flowTraceService ?? throw new ArgumentNullException(nameof(flowTraceService));
    }

    [McpServerTool(Name = "trace_flow", Title = "Trace Flow", ReadOnly = true, Idempotent = true)]
    [Description("Traces call flow from/to a symbol. Use to understand code flow: upstream shows callers (who uses this), downstream shows callees (what this calls). Requires symbolId OR path+line+column. Direction: 'upstream', 'downstream', or 'both' (default). Depth: how many hops to traverse (default 2, max unbounded). Returns call graph edges with locations.")]
    public Task<TraceFlowResult> TraceFlowAsync(
        CancellationToken cancellationToken,
        [Description("Symbol selector mode A: canonical symbolId. Use this, or provide path+line+column.")]
        string? symbolId = null,
        [Description("Symbol selector mode B: source file path used with line+column.")]
        string? path = null,
        [Description("Symbol selector mode B: 1-based line number used with path+column.")]
        int? line = null,
        [Description("Symbol selector mode B: 1-based column number used with path+line.")]
        int? column = null,
        [Description("Traversal direction: upstream, downstream, or both. Aliases up/down are accepted. Default is both.")]
        string? direction = null,
        [Description("Traversal depth as a non-negative integer. Defaults to 2 when omitted; values below 1 execute as depth 1.")]
        int? depth = null)
        => _flowTraceService.TraceFlowAsync(
            ToolContractMapper.ToTraceFlowRequest(symbolId, path, line, column, direction, depth),
            cancellationToken);
}
