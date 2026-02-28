using RoslynMcp.Core.Models.Common;

namespace RoslynMcp.Infrastructure.Workspace;

internal interface ISessionWorkspaceLoader
{
    Task<(SessionState? Session, ErrorInfo? Error)> TryLoadSessionAsync(string solutionPath, string workspaceRoot, CancellationToken ct);
}
