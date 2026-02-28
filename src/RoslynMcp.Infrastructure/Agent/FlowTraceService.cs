using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;

namespace RoslynMcp.Infrastructure.Agent;

public sealed class FlowTraceService : IFlowTraceService
{
    private readonly INavigationService _navigationService;

    public FlowTraceService(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public async Task<TraceFlowResult> TraceFlowAsync(TraceFlowRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var directionValidation = NormalizeDirection(request.Direction);
        if (directionValidation.Error != null)
        {
            return new TraceFlowResult(
                null,
                directionValidation.Direction,
                Math.Max(request.Depth ?? 2, 1),
                Array.Empty<CallEdge>(),
                Array.Empty<FlowTransition>(),
                directionValidation.Error);
        }

        var direction = directionValidation.Direction;
        var depth = Math.Max(request.Depth ?? 2, 1);

        var root = await ResolveRootSymbolAsync(request, ct).ConfigureAwait(false);
        if (root.Symbol == null)
        {
            return new TraceFlowResult(
                null,
                direction,
                depth,
                Array.Empty<CallEdge>(),
                Array.Empty<FlowTransition>(),
                AgentErrorInfo.Normalize(root.Error, "Call trace_flow with a resolvable symbolId or source position."));
        }

        IReadOnlyList<CallEdge> edges;
        if (string.Equals(direction, "upstream", StringComparison.Ordinal))
        {
            var callers = await _navigationService.GetCallersAsync(new GetCallersRequest(root.Symbol.SymbolId, depth), ct).ConfigureAwait(false);
            if (callers.Error != null)
            {
                return new TraceFlowResult(
                    root.Symbol,
                    direction,
                    depth,
                    Array.Empty<CallEdge>(),
                    Array.Empty<FlowTransition>(),
                    AgentErrorInfo.Normalize(callers.Error, "Retry trace_flow with a resolvable symbol and supported upstream traversal depth."));
            }

            edges = callers.Callers;
        }
        else if (string.Equals(direction, "downstream", StringComparison.Ordinal))
        {
            var callees = await _navigationService.GetCalleesAsync(new GetCalleesRequest(root.Symbol.SymbolId, depth), ct).ConfigureAwait(false);
            if (callees.Error != null)
            {
                return new TraceFlowResult(
                    root.Symbol,
                    direction,
                    depth,
                    Array.Empty<CallEdge>(),
                    Array.Empty<FlowTransition>(),
                    AgentErrorInfo.Normalize(callees.Error, "Retry trace_flow with a resolvable symbol and supported downstream traversal depth."));
            }

            edges = callees.Callees;
        }
        else
        {
            var graph = await _navigationService.GetCallGraphAsync(new GetCallGraphRequest(root.Symbol.SymbolId, "both", depth), ct).ConfigureAwait(false);
            if (graph.Error != null)
            {
                return new TraceFlowResult(
                    root.Symbol,
                    direction,
                    depth,
                    Array.Empty<CallEdge>(),
                    Array.Empty<FlowTransition>(),
                    AgentErrorInfo.Normalize(graph.Error, "Retry trace_flow with a resolvable symbol and supported traversal depth."));
            }

            edges = graph.Edges;
        }

        var transitions = edges
            .GroupBy(edge => (From: ExtractProject(edge.FromSymbolId), To: ExtractProject(edge.ToSymbolId)))
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key.From, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.To, StringComparer.Ordinal)
            .Select(group => new FlowTransition(group.Key.From, group.Key.To, group.Count()))
            .ToArray();

        return new TraceFlowResult(root.Symbol, direction, depth, edges, transitions);
    }

    private async Task<FindSymbolResult> ResolveRootSymbolAsync(TraceFlowRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.SymbolId))
        {
            return await _navigationService.FindSymbolAsync(new FindSymbolRequest(request.SymbolId), ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
        {
            var atPosition = await _navigationService.GetSymbolAtPositionAsync(
                new GetSymbolAtPositionRequest(request.Path, request.Line.Value, request.Column.Value),
                ct).ConfigureAwait(false);
            return new FindSymbolResult(atPosition.Symbol, atPosition.Error);
        }

        return new FindSymbolResult(
            null,
            AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "Provide symbolId or path/line/column.",
                "Call trace_flow with a symbolId or source position."));
    }

    private static (string Direction, ErrorInfo? Error) NormalizeDirection(string? direction)
    {
        var normalized = string.IsNullOrWhiteSpace(direction) ? "both" : direction.Trim().ToLowerInvariant();
        return normalized switch
        {
            "upstream" or "up" => ("upstream", null),
            "downstream" or "down" => ("downstream", null),
            "both" => ("both", null),
            _ => ("both", AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "direction must be one of: upstream, downstream, both.",
                "Retry trace_flow with direction set to upstream, downstream, or both.",
                ("field", "direction"),
                ("provided", direction ?? string.Empty),
                ("expected", "upstream|downstream|both")))
        };
    }

    private static string ExtractProject(string symbolId)
    {
        var separator = symbolId.IndexOf(':');
        return separator > 0 ? symbolId[..separator] : "unknown";
    }
}
