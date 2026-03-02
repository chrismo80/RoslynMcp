using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Navigation;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class GetTypeHierarchyTools
{
    private readonly INavigationService _navigationService;

    public GetTypeHierarchyTools(INavigationService navigationService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }

    [McpServerTool(Name = "get_type_hierarchy", Title = "Get Type Hierarchy", ReadOnly = true, Idempotent = true)]
    [Description("Gets the complete type hierarchy for a type: base classes, implemented interfaces, and derived types. Use to understand inheritance relationships and type evolution. Requires symbolId from resolve_symbol, list_types, or list_members.")]    public Task<GetTypeHierarchyResult> GetTypeHierarchyAsync(
        CancellationToken cancellationToken,
        [Description("Canonical symbolId from resolve_symbol, list_types, or list_members. Must resolve to a type (class, interface, enum, struct, record).")]
        string symbolId,
        [Description("When true (default), includes all transitive base types and derived types. When false, only immediate parents/children.")]
        bool includeTransitive = true,
        [Description("Maximum number of derived types to return. Default 200. Higher values may impact performance.")]
        int maxDerived = 200)
        => _navigationService.GetTypeHierarchyAsync(
            ToolContractMapper.ToGetTypeHierarchyRequest(symbolId, includeTransitive, maxDerived),
            cancellationToken);
}
