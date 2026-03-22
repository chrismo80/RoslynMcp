using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.GetTypeHierarchy;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "get_type_hierarchy", Title = "Get Type Hierarchy", ReadOnly = true, Idempotent = true)]
	[Description("Use this tool when you need to inspect a type's inheritance relationships: base types, implemented interfaces, and derived types. Use includeTransitive=false for immediate parents and children only, or true to expand the full transitive hierarchy.")]
	public Task<Result> Run(
		CancellationToken cancellationToken,
		[Description("The stable symbol ID of a type, obtained from resolve_symbol, list_types, or list_members. Must resolve to a type (class, interface, enum, struct, or record).")]
		string symbolId,
		[Description("When true (default), includes all transitive base types and all derived types. When false, returns only immediate parents and children.")]
		bool includeTransitive = true,
		[Description("Maximum number of derived types to return. Defaults to 200. Higher values may impact performance.")]
		int maxDerived = 200)
		=> service.RunAsync(symbolId, includeTransitive, maxDerived, cancellationToken);
}
