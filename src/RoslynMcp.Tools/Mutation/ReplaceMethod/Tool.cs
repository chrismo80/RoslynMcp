using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Mutation.ReplaceMethod;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "replace_method", Title = "Replace Method")]
	[Description("Use this tool when you need to rewrite an existing method structurally without manually editing the full file. Provide the target method symbol id plus flat replacement declaration fields and a method body string. The body can include multiple statements and complex control flow as long as it is valid for that method shape. The tool replaces the method structurally, formats the changed document, applies the solution, and returns a new method symbol id to use for later operations plus newly introduced diagnostics for the changed document.")]
	public Task<Result> Run(CancellationToken cancellationToken, string targetMethodSymbolId, string name, string returnType, string accessibility, IReadOnlyList<string>? modifiers, IReadOnlyList<string>? parameters, string body)
		=> service.RunAsync(targetMethodSymbolId.ToRequest(name, returnType, accessibility, modifiers, parameters, body), cancellationToken);
}
