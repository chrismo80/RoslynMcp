using ProjectCore;

namespace ProjectImpl;

public sealed class RoundRobinWorker : IWorker
{
    public int InvocationCount { get; private set; }

    public void Work()
    {
        InvocationCount++;
    }
}

public sealed class FastWorkItemOperation : OperationBase<WorkItem>, IRenamedWorkItemOperation
{
    public override async Task<OperationResult> ExecuteAsync(WorkItem input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RaiseStep("Fast.Validate", input);
        await DelayAsync(cancellationToken);
        RaiseStep("Fast.Complete", input);
        return new OperationResult(true, $"Fast:{input.Name}");
    }

    public Task<OperationResult> ExecuteAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(new WorkItem(id, name, Priority: 1), cancellationToken);
    }

    public Task<OperationResult> ExecuteAsync(Guid id, string name, int priority, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(new WorkItem(id, name, Priority: priority), cancellationToken);
    }
}

public sealed class SafeWorkItemOperation : OperationBase<WorkItem>, IRenamedWorkItemOperation
{
    public override async Task<OperationResult> ExecuteAsync(WorkItem input, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(input, cancellationToken);
        RaiseStep("Safe.Persist", validated);
        await DelayAsync(cancellationToken);
        return new OperationResult(true, $"Safe:{validated.Name}");
    }

    private async Task<WorkItem> ValidateAsync(WorkItem input, CancellationToken cancellationToken)
    {
        RaiseStep("Safe.Validate", input);
        await DelayAsync(cancellationToken);
        return input.Priority < 0 ? input with { Priority = 0 } : input;
    }
}

public sealed class DefaultFactory<T> : IFactory<T> where T : class, new()
{
    public T Create()
    {
        return new T();
    }
}
