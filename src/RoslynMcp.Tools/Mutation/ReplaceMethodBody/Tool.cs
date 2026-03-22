using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Mutation.ReplaceMethodBody;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "replace_method_body", Title = "Replace Method Body")]
	[Description("Use this tool when you need a targeted logic change that preserves an existing method declaration shape. It works on block-bodied methods only, not expression-bodied ones. Provide the target method symbol id and a body string containing the replacement statements. The body can include multiple statements and complex control flow as long as it is valid for that method shape. The tool preserves the declaration shape, replaces only the body node, formats the changed document, applies the solution, and returns changed files plus newly introduced diagnostics for the changed document.")]
	public Task<Result> Run(CancellationToken cancellationToken, string targetMethodSymbolId, string body)
		=> service.RunAsync(targetMethodSymbolId.ToRequest(body), cancellationToken);
}
