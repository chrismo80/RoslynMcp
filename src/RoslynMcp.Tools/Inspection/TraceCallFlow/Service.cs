using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Infrastructure.Services;

namespace RoslynMcp.Tools.Inspection.TraceCallFlow;

public sealed class Service(RoslynMcp.Tools.Infrastructure.Services.Workspace workspace)
{
    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        var (direction, directionError) = request.Direction.NormalizeDirection();
        var depth = Math.Max(request.Depth ?? 2, 1);
        if (directionError is not null)
            return new Result(null, null, direction, depth, null, [], null, null, null, directionError);

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
            return new Result(null, null, direction, depth, null, [], null, null, null, new ErrorInfo("no_solution_loaded", "No solution is currently loaded."));

        var root = await ResolveRootAsync(request, session.Solution, cancellationToken).ConfigureAwait(false);
        if (root is null)
            return new Result(null, null, direction, depth, null, [], null, null, null, new ErrorInfo("invalid_input", "Call trace_call_flow with a resolvable symbolId or source position."));

        var edges = direction switch
        {
            FlowDirections.Upstream => await root.GetCallersAsync(session.Solution, depth, cancellationToken).ConfigureAwait(false),
            FlowDirections.Downstream => await root.GetCalleesAsync(session.Solution, depth, cancellationToken).ConfigureAwait(false),
            _ =>

            [
                .. (await root.GetCallersAsync(session.Solution, depth, cancellationToken).ConfigureAwait(false))
,
                .. await root.GetCalleesAsync(session.Solution, depth, cancellationToken).ConfigureAwait(false),
            ]
        };

        var possibleTargetEdges = request.IncludePossibleTargets
            ? await root.GetPossibleTargetsAsync(session.Solution, cancellationToken).ConfigureAwait(false)
            : [];

        var edgeList = edges.Select(static edge => new TraceFlowEdge(
            edge.From.ToStableId(),
            edge.To.ToStableId(),
            edge.Location,
            FlowEvidenceKinds.DirectStatic,
            IsInterfaceDispatch(edge.To) ? [FlowUncertaintyCategories.InterfaceDispatch] : null)).ToArray();
        var possibleTargetEdgeList = possibleTargetEdges
            .Select(static edge => new TraceFlowEdge(edge.From.ToStableId(), edge.To.ToStableId(), edge.Location, FlowEvidenceKinds.PossibleTarget, [FlowUncertaintyCategories.InterfaceDispatch]))
            .ToArray();
        var symbolTable = edges
            .SelectMany(static edge => new[] { edge.From, edge.To })
            .Concat(possibleTargetEdges.SelectMany(static edge => new[] { edge.From, edge.To }))
            .Append(root)
            .GroupBy(static symbol => symbol.ToStableId(), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().ToTraceSymbolEntry(), StringComparer.Ordinal);

        var transitions = edges
            .GroupBy(static edge => (From: edge.From.ContainingAssembly?.Name ?? "unresolved_project", To: edge.To.ContainingAssembly?.Name ?? "unresolved_project"))
            .Select(static group => new FlowTransition(group.Key.From, group.Key.To, group.Count()))
            .OrderByDescending(static group => group.Count)
            .ToArray();

        return new Result(root.ToStableId(), root.ToRootSummary(), direction, depth, symbolTable, edgeList, possibleTargetEdgeList.Length == 0 ? null : possibleTargetEdgeList, transitions.Length == 0 ? null : transitions, null).WithWorkspaceRelativePaths();
    }

    private static bool IsInterfaceDispatch(ISymbol symbol)
        => symbol is IMethodSymbol { ContainingType.TypeKind: TypeKind.Interface };

    private async Task<ISymbol?> ResolveRootAsync(Request request, Solution solution, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SymbolId))
            return await SymbolLookup.ResolveSymbolAsync(request.SymbolId!, solution, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
            return await SymbolLookup.GetSymbolAtPositionAsync(solution, request.Path!, request.Line.Value, request.Column.Value, cancellationToken).ConfigureAwait(false);

        return null;
    }
}
