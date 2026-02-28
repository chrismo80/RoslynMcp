using RoslynMcp.Core.Models.Navigation;

namespace RoslynMcp.Core.Contracts;

public interface INavigationService
{
    Task<FindSymbolResult> FindSymbolAsync(FindSymbolRequest request, CancellationToken ct);
    Task<GetSymbolAtPositionResult> GetSymbolAtPositionAsync(GetSymbolAtPositionRequest request, CancellationToken ct);
    Task<SearchSymbolsResult> SearchSymbolsAsync(SearchSymbolsRequest request, CancellationToken ct);
    Task<SearchSymbolsScopedResult> SearchSymbolsScopedAsync(SearchSymbolsScopedRequest request, CancellationToken ct);
    Task<GetSignatureResult> GetSignatureAsync(GetSignatureRequest request, CancellationToken ct);
    Task<FindReferencesResult> FindReferencesAsync(FindReferencesRequest request, CancellationToken ct);
    Task<FindReferencesScopedResult> FindReferencesScopedAsync(FindReferencesScopedRequest request, CancellationToken ct);
    Task<FindImplementationsResult> FindImplementationsAsync(FindImplementationsRequest request, CancellationToken ct);
    Task<GetTypeHierarchyResult> GetTypeHierarchyAsync(GetTypeHierarchyRequest request, CancellationToken ct);
    Task<GetSymbolOutlineResult> GetSymbolOutlineAsync(GetSymbolOutlineRequest request, CancellationToken ct);
    Task<GetCallersResult> GetCallersAsync(GetCallersRequest request, CancellationToken ct);
    Task<GetCalleesResult> GetCalleesAsync(GetCalleesRequest request, CancellationToken ct);
    Task<GetCallGraphResult> GetCallGraphAsync(GetCallGraphRequest request, CancellationToken ct);
}
