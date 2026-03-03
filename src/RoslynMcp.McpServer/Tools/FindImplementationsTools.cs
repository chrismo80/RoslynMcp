using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Navigation;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class FindImplementationsTools
{
    private readonly INavigationService _navigationService;

    public FindImplementationsTools(INavigationService navigationService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }

    [McpServerTool(Name = "find_implementations", Title = "Find Implementations", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to find all implementations of an interface, abstract class, or abstract/virtual method. This is essential for understanding polymorphism — where interfaces are implemented or where abstract members are overridden.")]
    public Task<FindImplementationsResult> FindImplementationsAsync(
        CancellationToken cancellationToken,
        [Description("The stable symbol ID of an interface, abstract class, or abstract/virtual method, obtained from resolve_symbol, list_types, or list_members.")]
        string symbolId)
        => _navigationService.FindImplementationsAsync(
            ToolContractMapper.ToFindImplementationsRequest(symbolId),
            cancellationToken);
}
