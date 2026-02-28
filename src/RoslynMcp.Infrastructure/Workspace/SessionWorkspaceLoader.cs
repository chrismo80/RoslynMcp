using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Infrastructure.Workspace;

internal sealed class SessionWorkspaceLoader : ISessionWorkspaceLoader
{
    private readonly IMSBuildRegistrationGate _registrationGate;
    private readonly ILogger<SessionWorkspaceLoader> _logger;

    public SessionWorkspaceLoader(IMSBuildRegistrationGate registrationGate, ILogger<SessionWorkspaceLoader>? logger = null)
    {
        _registrationGate = registrationGate ?? throw new ArgumentNullException(nameof(registrationGate));
        _logger = logger ?? NullLogger<SessionWorkspaceLoader>.Instance;
    }

    public async Task<(SessionState? Session, ErrorInfo? Error)> TryLoadSessionAsync(
        string solutionPath,
        string workspaceRoot,
        CancellationToken ct)
    {
        _registrationGate.EnsureRegistered();

        MSBuildWorkspace? workspace = null;
        try
        {
            workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath, progress: null, cancellationToken: ct).ConfigureAwait(false);
            return (new SessionState(workspaceRoot, solutionPath, workspace, solution), null);
        }
        catch (OperationCanceledException)
        {
            workspace?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            workspace?.Dispose();
            _logger.LogError(ex, "Failed to load solution {SolutionPath}", solutionPath);
            return (null, new ErrorInfo(ErrorCodes.InternalError,
                $"Failed to load solution '{solutionPath}': {ex.Message}"));
        }
    }
}
