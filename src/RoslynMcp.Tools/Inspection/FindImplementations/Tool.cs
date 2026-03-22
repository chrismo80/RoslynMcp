using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.FindImplementations;

[McpServerToolType]
public sealed class Tool(Service service)
{
    [McpServerTool(Name = "find_implementations", Title = "Find Implementations", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to find concrete implementations of an interface or abstract type, and overrides or implementations of abstract/virtual members. This is essential for understanding static polymorphic targets in the loaded solution before refactoring or changing a contract.")]
    public Task<Result> Run(
        CancellationToken cancellationToken,
        [Description("The stable symbol ID of an interface, abstract type, or abstract/virtual member, obtained from resolve_symbol, list_types, or list_members.")]
        string symbolId)
        => service.RunAsync(symbolId, cancellationToken);
}
