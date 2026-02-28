namespace RoslynMcp.Infrastructure.Workspace;

internal interface ISessionStateStore
{
    Task<T> WithLockAsync<T>(Func<SessionStateStore.StateSnapshot, T> action, CancellationToken ct);

    Task<T> WithLockAsync<T>(Func<SessionStateStore.StateSnapshot, Task<T>> action, CancellationToken ct);

    Task SetWorkspaceRootHintAsync(string workspaceRoot, CancellationToken ct);

    string? GetWorkspaceRootHintUnsafe();
}
