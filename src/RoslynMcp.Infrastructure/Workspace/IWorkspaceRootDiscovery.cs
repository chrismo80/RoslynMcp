using RoslynMcp.Core.Models.Common;

namespace RoslynMcp.Infrastructure.Workspace;

internal interface IWorkspaceRootDiscovery
{
    (string? NormalizedRoot, ErrorInfo? Error) NormalizeWorkspaceRoot(string? workspaceRoot);

    Task<(IReadOnlyList<string> Solutions, ErrorInfo? Error)> DiscoverSolutionsAsync(string normalizedRoot, CancellationToken ct);
}
