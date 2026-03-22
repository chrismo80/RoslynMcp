using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.ResolveSymbols;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "resolve_symbols", Title = "Resolve Symbols", ReadOnly = true, Idempotent = true)]
	[Description("Use this tool when you need to resolve multiple symbols in one round-trip. Each entry reuses resolve_symbol semantics, including symbolId, source position, qualifiedName lookup, project scoping, readable symbol references, and structured ambiguity results.")]
	public Task<Result> Run(
		CancellationToken cancellationToken,
		[Description("The symbols to resolve. Each entry supports the same selector modes as resolve_symbol: symbolId, path+line+column, or qualifiedName with optional project scoping.")]
		IReadOnlyList<Entry> entries)
		=> service.RunAsync(new Request(entries), cancellationToken);
}
