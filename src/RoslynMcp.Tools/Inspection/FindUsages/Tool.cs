using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.FindUsages;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "find_usages", Title = "Find Usages", ReadOnly = true, Idempotent = true)]
	[Description("Use this tool when you need to find source-code references to a specific symbol across a document, project, or the entire solution. This is critical before refactoring or modifying a symbol to understand its static impact, but it may not include dynamic, reflection-based, or string-based usages.")]
	public Task<Result> Run(
		CancellationToken cancellationToken,
		[Description("The stable symbol ID, obtained from resolve_symbol, list_types, or list_members.")]
		string symbolId,
		[Description("The search scope. project searches only within the containing project. solution searches the entire solution. Defaults to solution.")]
		string scope = "solution",
		[Description("Required when scope=document: the file path to search within.")]
		string? path = null)
		=> service.RunAsync(new Request(symbolId, scope.NormalizeScope(), path.NormalizeOptional()), cancellationToken);
}
