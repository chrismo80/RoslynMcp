using ProjectCore;
using ProjectImpl;

namespace ProjectApp;

public sealed class AppOrchestrator(IRenamedWorkItemOperation operation)
{
    private static readonly Guid SampleId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly IRenamedWorkItemOperation _operation = operation;
    private readonly ProcessingSession _session = new();
    private readonly CodeSmells _smells = new();
    private int _steps;

    public async Task<OperationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        _operation.StepCompleted += OnStepCompleted;
        _session.StateChanged += OnStateChanged;

        await _session.StartAsync(cancellationToken);
        var item = new WorkItem(SampleId, "direct", Priority: 2);
        var directResult = await ExecuteFlowAsync(item, cancellationToken);

        _ = _smells.Calculate(3);

        _session.Stop();
        _session.StateChanged -= OnStateChanged;
        _operation.StepCompleted -= OnStepCompleted;

        return directResult with { Message = $"{directResult.Message}|Steps:{_steps}" };
    }

    public async Task<OperationResult?> RunReflectionPathAsync(CancellationToken cancellationToken = default)
    {
        var method = _operation.GetType().GetMethod(
            nameof(FastWorkItemOperation.ExecuteAsync),
            [typeof(Guid), typeof(string), typeof(CancellationToken)]);

        if (method is null)
        {
            return null;
        }

        var invoked = method.Invoke(_operation, [SampleId, "reflect", cancellationToken]);
        if (invoked is Task<OperationResult> operationTask)
        {
            return await operationTask;
        }

        return null;
    }

    private Task<OperationResult> ExecuteFlowAsync(WorkItem item, CancellationToken cancellationToken)
    {
        return _operation.ExecuteAsync(item, cancellationToken);
    }

    private void OnStepCompleted(object? sender, StepEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.StepName))
        {
            _steps++;
        }
    }

    private void OnStateChanged(object? sender, string state)
    {
        if (state == "Stopped")
        {
            _steps++;
        }
    }
}

public static class AppEntryPoints
{
    public static Task<OperationResult> RunFastAsync(CancellationToken cancellationToken = default)
    {
        return new AppOrchestrator(new FastWorkItemOperation()).RunAsync(cancellationToken);
    }

    public static Task<OperationResult> RunSafeAsync(CancellationToken cancellationToken = default)
    {
        return new AppOrchestrator(new SafeWorkItemOperation()).RunAsync(cancellationToken);
    }
}
