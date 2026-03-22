using RoslynMcp.Tools.Inspection.TraceCallFlow;

namespace RoslynMcp.Tools.Inspection.FindCallers;

public sealed class Service(TraceCallFlow.Service traceCallFlow)
{
    public Task<Result> RunAsync(string? symbolId, CancellationToken cancellationToken)
        => traceCallFlow.RunAsync(new Request(symbolId.NormalizeOptional(), null, null, null, FlowDirections.Upstream, 1, false), cancellationToken);
}
