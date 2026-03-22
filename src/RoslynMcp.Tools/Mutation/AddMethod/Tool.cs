using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Mutation.AddMethod;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "add_method", Title = "Add Method")]
	[Description("Use this tool when you need to add a new helper, overload, or other method to an existing loaded type without rewriting the full file. Provide the target type symbol id, flat method signature fields, and a method body string. The body can include multiple statements and complex control flow as long as it is valid for that method shape. The tool inserts the method structurally, formats the changed document, applies the solution, and returns the created method symbol id plus newly introduced diagnostics for the changed document.")]
	public Task<Result> Run(CancellationToken cancellationToken, string targetTypeSymbolId, string name, string returnType, string accessibility, IReadOnlyList<string>? modifiers, IReadOnlyList<string>? parameters, string body)
		=> service.RunAsync(targetTypeSymbolId.ToRequest(name, returnType, accessibility, modifiers, parameters, body), cancellationToken);
}
