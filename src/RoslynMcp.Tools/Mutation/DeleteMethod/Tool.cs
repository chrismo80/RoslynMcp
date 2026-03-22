using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Mutation.DeleteMethod;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "delete_method", Title = "Delete Method")]
	[Description("Use this tool when you need to remove an exact method declaration from a loaded solution without manually rewriting the full file, such as cleaning up disposable helpers or obsolete overloads. Provide the stable symbol id of the exact method to remove. The tool resolves that method semantically, removes its source declaration structurally, formats the changed document, applies the solution, and returns changed files plus newly introduced diagnostics for the changed document.")]
	public Task<Result> Run(CancellationToken cancellationToken, string targetMethodSymbolId)
		=> service.RunAsync(targetMethodSymbolId.ToRequest(), cancellationToken);
}
