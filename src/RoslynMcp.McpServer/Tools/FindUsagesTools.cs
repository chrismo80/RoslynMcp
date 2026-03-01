using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Navigation;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class FindUsagesTools
{
    private readonly INavigationService _navigationService;

    public FindUsagesTools(INavigationService navigationService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }

    [McpServerTool(Name = "find_usages", Title = "Find Usages", ReadOnly = true, Idempotent = true)]
    [Description("Finds all references/usages of a symbol across the solution. Use to locate where a type, method, property, or field is being used. Returns reference locations with file paths and line numbers. Requires symbolId from resolve_symbol or list_types/list_members.")]
    public Task<FindReferencesResult> FindUsagesAsync(
        CancellationToken cancellationToken,
        [Description("Canonical symbolId from resolve_symbol, list_types, or list_members. Use this to identify the symbol whose usages you want to find.")]
        string symbolId)
        => _navigationService.FindReferencesAsync(
            ToolContractMapper.ToFindReferencesRequest(symbolId),
            cancellationToken);

    [McpServerTool(Name = "find_usages_scoped", Title = "Find Usages (Scoped)", ReadOnly = true, Idempotent = true)]
    [Description("Finds references/usages of a symbol within a specific scope: 'document', 'project', or 'solution'. Use when you want to limit search to a specific file or project. Requires symbolId and scope.")]
    public Task<FindReferencesScopedResult> FindUsagesScopedAsync(
        CancellationToken cancellationToken,
        [Description("Canonical symbolId from resolve_symbol, list_types, or list_members.")]
        string symbolId,
        [Description("Search scope: 'document' (current file only), 'project' (containing project), or 'solution' (all projects).")]
        string scope = "solution",
        [Description("Required when scope='document': the file path to search within.")]
        string? path = null)
        => _navigationService.FindReferencesScopedAsync(
            ToolContractMapper.ToFindReferencesScopedRequest(symbolId, scope, path),
            cancellationToken);
}
