namespace RoslynMcp.Infrastructure.Workspace;

internal sealed class SessionStateStore : ISessionStateStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private SessionState? _currentSession;
    private string? _workspaceRootHint;
    private int _workspaceVersion;

    public string? GetWorkspaceRootHintUnsafe()
        => _workspaceRootHint;

    public async Task SetWorkspaceRootHintAsync(string workspaceRoot, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _workspaceRootHint = workspaceRoot;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T> WithLockAsync<T>(Func<StateSnapshot, T> action, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return action(CreateSnapshot());
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T> WithLockAsync<T>(Func<StateSnapshot, Task<T>> action, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await action(CreateSnapshot()).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private StateSnapshot CreateSnapshot()
        => new(_currentSession, _workspaceRootHint, _workspaceVersion, SetInternalState);

    private void SetInternalState(SessionState? session, string? workspaceRootHint, int workspaceVersion)
    {
        _currentSession = session;
        _workspaceRootHint = workspaceRootHint;
        _workspaceVersion = workspaceVersion;
    }

    public readonly record struct StateSnapshot(
        SessionState? CurrentSession,
        string? WorkspaceRootHint,
        int WorkspaceVersion,
        Action<SessionState?, string?, int> SetState)
    {
        public void Update(SessionState? session, string? workspaceRootHint, int workspaceVersion)
            => SetState(session, workspaceRootHint, workspaceVersion);
    }
}
