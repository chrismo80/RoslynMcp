using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.TraceCallFlow;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "trace_call_flow", Title = "Trace Call Flow", ReadOnly = true, Idempotent = true)]
	[Description("Use this tool when you need to understand how code flows through your system — either finding what calls a specific symbol (upstream) or what a symbol calls (downstream). Results prefer hand-written source by default so generated/intermediate call edges do not overwhelm interactive traces, and transition labels now degrade explicitly to unresolved_project/project_inference_degraded when attribution is uncertain. Set includePossibleTargets=true to receive a deliberate possible-runtime-target edge set for uncertain polymorphic dispatch.")]
	public Task<Result> Run(CancellationToken cancellationToken, string? symbolId = null, string? path = null, int? line = null, int? column = null, string? direction = null, int? depth = null, bool? includePossibleTargets = null)
		=> service.RunAsync(new Request(symbolId.NormalizeOptional(), path.NormalizeOptional(), line, column, direction.NormalizeOptional(), depth, includePossibleTargets ?? false), cancellationToken);
}
