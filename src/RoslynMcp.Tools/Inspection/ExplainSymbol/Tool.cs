using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.ExplainSymbol;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "explain_symbol", Title = "Explain Symbol", ReadOnly = true, Idempotent = true)]
	[Description("Use this tool when you need to understand what a specific symbol (type, method, property, field, etc.) does, what its signature looks like, where it is used in the codebase, and what XML documentation it already exposes. It provides a human-readable explanation along with impact hints showing areas with high reference density.")]
	public Task<Result> Run(
		CancellationToken cancellationToken,
		[Description("The stable symbol ID, obtained from resolve_symbol, list_types, or list_members. Provide this OR path+line+column.")]
		string? symbolId = null,
		[Description("Path to a source file. Provide this together with line and column instead of symbolId.")]
		string? path = null,
		[Description("Line number (1-based) pointing to the symbol in the source file.")]
		int? line = null,
		[Description("Column number (1-based) pointing to the symbol in the source file.")]
		int? column = null)
		=> service.RunAsync(new Request(symbolId.NormalizeOptional(), path.NormalizeOptional(), line, column), cancellationToken);
}
