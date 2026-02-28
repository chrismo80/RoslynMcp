using RoslynMcp.Core.Models.Common;

namespace RoslynMcp.Core.Contracts;

public interface ISolutionSessionService
{
    Task<DiscoverSolutionsResult> DiscoverSolutionsAsync(DiscoverSolutionsRequest request, CancellationToken ct);
    Task<SelectSolutionResult> SelectSolutionAsync(SelectSolutionRequest request, CancellationToken ct);
    Task<ReloadSolutionResult> ReloadSolutionAsync(ReloadSolutionRequest request, CancellationToken ct);
}
