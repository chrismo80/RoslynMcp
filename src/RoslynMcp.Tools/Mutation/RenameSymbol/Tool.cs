using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Mutation.RenameSymbol;

[McpServerToolType]
public sealed class Tool(Service service)
{
    [McpServerTool(Name = "rename_symbol", Title = "Rename Symbol")]
    [Description("Use this tool when you need to rename a symbol (type, method, property, field, parameter, local variable, etc.) across the entire solution. This performs a safe refactoring that updates all references to the symbol.  Best for: When you don't know all places where a type is used - it finds every reference automatically.")]
    public Task<Result> Run(CancellationToken cancellationToken,
        [Description("The symbol ID of the symbol to rename. Use 'resolve_symbol' to obtain this if needed.")]
        string symbolId,
        [Description("The new name for the symbol. Must be a valid C# identifier and should not conflict with existing symbols in the same scope.")]
        string newName)
        => service.RunAsync(symbolId.ToRequest(newName), cancellationToken);
}
