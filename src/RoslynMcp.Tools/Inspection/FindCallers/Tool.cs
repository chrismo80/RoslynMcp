using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Inspection.TraceCallFlow;

namespace RoslynMcp.Tools.Inspection.FindCallers;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "find_callers", Title = "Find Callers", ReadOnly = true, Idempotent = true)]
	[Description("Use this tool when you need only the immediate direct upstream callers of a symbol. This is a focused wrapper around call-flow tracing and does not traverse beyond one caller level.")]
	public Task<Result> Run(CancellationToken cancellationToken, [Description("The stable symbol ID, obtained from resolve_symbol, list_types, or list_members, for the symbol whose immediate direct callers you want to inspect.")] string? symbolId = null)
		=> service.RunAsync(symbolId, cancellationToken);
}
