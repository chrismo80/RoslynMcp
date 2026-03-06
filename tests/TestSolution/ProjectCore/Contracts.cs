namespace ProjectCore;

public delegate void OperationLoggedHandler(object? sender, string message);

public record WorkItem(Guid Id, string Name, int Priority);

public record OperationResult(bool Success, string Message);

public sealed class StepEventArgs(string stepName, WorkItem? item = null) : EventArgs
{
    public string StepName { get; } = stepName;

    public WorkItem? Item { get; } = item;
}

public interface IOperation<in TInput, TResult> where TInput : notnull
{
    Task<TResult> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}

public interface ITrackedOperation<in TInput, TResult> : IOperation<TInput, TResult> where TInput : notnull
{
    event EventHandler<StepEventArgs>? StepCompleted;
}

public interface IFactory<out T> where T : class, new()
{
    T Create();
}

public interface IRenamedWorkItemOperation : ITrackedOperation<WorkItem, OperationResult>
{
}

public abstract class OperationBase<TInput> : ITrackedOperation<TInput, OperationResult> where TInput : notnull
{
    public event EventHandler<StepEventArgs>? StepCompleted;

    public event OperationLoggedHandler? Logged;

    public abstract Task<OperationResult> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);

    protected void RaiseStep(string stepName, WorkItem? item = null)
    {
        Logged?.Invoke(this, stepName);
        StepCompleted?.Invoke(this, new StepEventArgs(stepName, item));
    }

    protected virtual Task DelayAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(5, cancellationToken);
    }
}
