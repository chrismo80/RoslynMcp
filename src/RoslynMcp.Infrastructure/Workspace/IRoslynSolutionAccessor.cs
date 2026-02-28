using RoslynMcp.Core.Models.Common;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Workspace;

public interface IRoslynSolutionAccessor
{
    Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct);

    Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct);

    Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct);
}
