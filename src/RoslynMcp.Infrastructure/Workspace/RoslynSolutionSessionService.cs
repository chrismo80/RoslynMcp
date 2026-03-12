using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Infrastructure.Workspace;

/// <summary>
/// Manages solution sessions: tracks current solution, handles loading/discovery.
/// Provides unified interface for solution access across services.
/// </summary>
public sealed class RoslynSolutionSessionService : ISolutionSessionService, IRoslynSolutionAccessor
{
    private const string StaleWorkspaceSnapshotMessage = "Workspace snapshot is stale relative to filesystem. Run reload_solution or load_solution, then retry.";

    private readonly ISessionStateStore _stateStore;
    private readonly IWorkspaceRootDiscovery _workspaceRootDiscovery;
    private readonly ISolutionPathResolver _solutionPathResolver;
    private readonly ISessionWorkspaceLoader _sessionWorkspaceLoader;
    private readonly ILogger<RoslynSolutionSessionService> _logger;

    internal RoslynSolutionSessionService(
        ISessionStateStore stateStore,
        IWorkspaceRootDiscovery workspaceRootDiscovery,
        ISolutionPathResolver solutionPathResolver,
        ISessionWorkspaceLoader sessionWorkspaceLoader,
        ILogger<RoslynSolutionSessionService>? logger = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _workspaceRootDiscovery = workspaceRootDiscovery ?? throw new ArgumentNullException(nameof(workspaceRootDiscovery));
        _solutionPathResolver = solutionPathResolver ?? throw new ArgumentNullException(nameof(solutionPathResolver));
        _sessionWorkspaceLoader = sessionWorkspaceLoader ?? throw new ArgumentNullException(nameof(sessionWorkspaceLoader));
        _logger = logger ?? NullLogger<RoslynSolutionSessionService>.Instance;
    }

    public RoslynSolutionSessionService(ILogger<RoslynSolutionSessionService>? logger = null)
        : this(
            new SessionStateStore(),
            new WorkspaceRootDiscovery(),
            new SolutionPathResolver(),
            new SessionWorkspaceLoader(new MsBuildRegistrationGate()),
            logger)
    {
    }

    public async Task<DiscoverSolutionsResult> DiscoverSolutionsAsync(DiscoverSolutionsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var (normalizedRoot, normalizeError) = _workspaceRootDiscovery.NormalizeWorkspaceRoot(request.WorkspaceRoot);
        if (normalizeError != null)
        {
            return new DiscoverSolutionsResult(Array.Empty<string>(), normalizeError);
        }

        var (solutions, discoverError) = await _workspaceRootDiscovery.DiscoverSolutionsAsync(normalizedRoot!, ct).ConfigureAwait(false);
        if (discoverError != null)
        {
            return new DiscoverSolutionsResult(Array.Empty<string>(), discoverError);
        }

        await _stateStore.SetWorkspaceRootHintAsync(normalizedRoot!, ct).ConfigureAwait(false);
        return new DiscoverSolutionsResult(solutions);
    }

    public async Task<SelectSolutionResult> SelectSolutionAsync(SelectSolutionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var workspaceRootHint = _stateStore.GetWorkspaceRootHintUnsafe();
        var (resolvedPath, workspaceRoot, resolutionError) = _solutionPathResolver.ResolveSolutionPath(request.SolutionPath, workspaceRootHint);
        if (resolutionError != null)
        {
            return new SelectSolutionResult(null, resolutionError);
        }

        var (session, loadError) = await _sessionWorkspaceLoader.TryLoadSessionAsync(resolvedPath!, workspaceRoot!, ct).ConfigureAwait(false);
        if (session == null)
        {
            return new SelectSolutionResult(null, loadError);
        }

        await _stateStore.WithLockAsync(snapshot =>
        {
            var previous = snapshot.CurrentSession;
            snapshot.Update(session, workspaceRoot, snapshot.WorkspaceVersion + 1);
            previous?.Dispose();
            return 0;
        }, ct).ConfigureAwait(false);

        return new SelectSolutionResult(resolvedPath);
    }

    public async Task<ReloadSolutionResult> ReloadSolutionAsync(ReloadSolutionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        return await _stateStore.WithLockAsync(async snapshot =>
        {
            if (snapshot.CurrentSession == null)
            {
                return new ReloadSolutionResult(false,
                    new ErrorInfo(ErrorCodes.SolutionNotSelected, "No solution has been selected."));
            }

            var previous = snapshot.CurrentSession;
            var (session, loadError) = await _sessionWorkspaceLoader
                .TryLoadSessionAsync(previous.SelectedSolutionPath, previous.WorkspaceRoot, ct)
                .ConfigureAwait(false);

            if (session == null)
            {
                return new ReloadSolutionResult(false, loadError);
            }

            snapshot.Update(session, snapshot.WorkspaceRootHint, snapshot.WorkspaceVersion + 1);
            previous.Dispose();
            return new ReloadSolutionResult(true);
        }, ct).ConfigureAwait(false);
    }

    public async Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ct.ThrowIfCancellationRequested();

        return await _stateStore.WithLockAsync(async snapshot =>
        {
            if (snapshot.CurrentSession == null)
            {
                return (false, (ErrorInfo?)new ErrorInfo(ErrorCodes.SolutionNotSelected, "No solution has been selected."));
            }

            var changedDocuments = solution.GetChanges(snapshot.CurrentSession.Solution)
                .GetProjectChanges()
                .SelectMany(static projectChange => projectChange.GetChangedDocuments())
                .Distinct()
                .Select(snapshot.CurrentSession.Solution.GetDocument)
                .OfType<Document>()
                .ToArray();

            var filesystemHealth = await WorkspaceDocumentFilesystemHealthEvaluator
                .EvaluateAsync(changedDocuments, ct)
                .ConfigureAwait(false);
            if (!filesystemHealth.IsConsistent)
            {
                return (false, (ErrorInfo?)new ErrorInfo(ErrorCodes.StaleWorkspaceSnapshot, StaleWorkspaceSnapshotMessage));
            }

            if (!snapshot.CurrentSession.Workspace.TryApplyChanges(solution))
            {
                _logger.LogWarning("Workspace rejected solution updates for {SolutionPath}", snapshot.CurrentSession.SelectedSolutionPath);
                return (false, (ErrorInfo?)new ErrorInfo(ErrorCodes.InternalError, "Failed to apply changes to the current solution."));
            }

            var appliedSolution = snapshot.CurrentSession.Workspace.CurrentSolution;
            snapshot.CurrentSession.UpdateSolution(appliedSolution);
            snapshot.Update(snapshot.CurrentSession, snapshot.WorkspaceRootHint, snapshot.WorkspaceVersion + 1);
            return (true, (ErrorInfo?)null);
        }, ct).ConfigureAwait(false);
    }

    public async Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await _stateStore.WithLockAsync(snapshot =>
        {
            if (snapshot.CurrentSession == null)
            {
                return ((Solution?)null, (ErrorInfo?)new ErrorInfo(ErrorCodes.SolutionNotSelected, "No solution has been selected."));
            }

            return ((Solution?)snapshot.CurrentSession.Solution, (ErrorInfo?)null);
        }, ct).ConfigureAwait(false);
    }

    public async Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await _stateStore.WithLockAsync(snapshot =>
        {
            if (snapshot.CurrentSession == null)
            {
                return (0, (ErrorInfo?)new ErrorInfo(ErrorCodes.SolutionNotSelected, "No solution has been selected."));
            }

            return (snapshot.WorkspaceVersion, (ErrorInfo?)null);
        }, ct).ConfigureAwait(false);
    }
}
