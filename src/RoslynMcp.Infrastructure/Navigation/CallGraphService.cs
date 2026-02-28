using RoslynMcp.Core.Models.Navigation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcp.Infrastructure.Navigation;

internal sealed class CallGraphService : ICallGraphService
{
    public bool IsValidDirection(string direction)
        => string.Equals(direction, CallGraphDirections.Incoming, StringComparison.Ordinal) ||
           string.Equals(direction, CallGraphDirections.Outgoing, StringComparison.Ordinal) ||
           string.Equals(direction, CallGraphDirections.Both, StringComparison.Ordinal);

    public Task<IReadOnlyList<CallEdge>> GetCallersAsync(ISymbol root, Solution solution, int maxDepth, CancellationToken ct)
        => BuildCallGraphAsync(root, solution, maxDepth, callers: true, ct);

    public Task<IReadOnlyList<CallEdge>> GetCalleesAsync(ISymbol root, Solution solution, int maxDepth, CancellationToken ct)
        => BuildCallGraphAsync(root, solution, maxDepth, callers: false, ct);

    public async Task<IReadOnlyList<CallEdge>> GetCallGraphAsync(ISymbol root,
        Solution solution,
        string direction,
        int maxDepth,
        CancellationToken ct)
    {
        var incoming = string.Equals(direction, CallGraphDirections.Incoming, StringComparison.Ordinal) ||
                       string.Equals(direction, CallGraphDirections.Both, StringComparison.Ordinal);
        var outgoing = string.Equals(direction, CallGraphDirections.Outgoing, StringComparison.Ordinal) ||
                       string.Equals(direction, CallGraphDirections.Both, StringComparison.Ordinal);

        var edgeMap = new Dictionary<string, CallEdge>(StringComparer.Ordinal);

        if (incoming)
        {
            var callers = await BuildCallGraphAsync(root, solution, maxDepth, callers: true, ct).ConfigureAwait(false);
            foreach (var edge in callers)
            {
                edgeMap[NavigationModelUtilities.GetEdgeKey(edge)] = edge;
            }
        }

        if (outgoing)
        {
            var callees = await BuildCallGraphAsync(root, solution, maxDepth, callers: false, ct).ConfigureAwait(false);
            foreach (var edge in callees)
            {
                edgeMap[NavigationModelUtilities.GetEdgeKey(edge)] = edge;
            }
        }

        return edgeMap.Values.OrderBy(static edge => edge, CallEdgeComparer.Instance).ToArray();
    }

    private static async Task<IReadOnlyList<CallEdge>> BuildCallGraphAsync(ISymbol root, Solution solution, int maxDepth, bool callers, CancellationToken ct)
    {
        var resolvedRoot = root.OriginalDefinition ?? root;
        var edges = new List<CallEdge>();
        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(ISymbol Symbol, int Depth)>();

        visited.Add(SymbolIdentity.CreateId(resolvedRoot));
        queue.Enqueue((resolvedRoot, 0));

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (current, depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            if (callers)
            {
                var callerInfos = await SymbolFinder.FindCallersAsync(current, solution, ct).ConfigureAwait(false);
                foreach (var info in callerInfos)
                {
                    var normalizedCalled = info.CalledSymbol.OriginalDefinition ?? info.CalledSymbol;
                    var normalizedCaller = info.CallingSymbol.OriginalDefinition ?? info.CallingSymbol;
                    var toId = SymbolIdentity.CreateId(normalizedCalled);
                    var callerId = SymbolIdentity.CreateId(normalizedCaller);

                    foreach (var location in info.Locations)
                    {
                        if (!location.IsInSource)
                        {
                            continue;
                        }

                        var source = NavigationModelUtilities.CreateSourceLocation(location);
                        var edge = new CallEdge(callerId, toId, source);
                        if (edgeKeys.Add(NavigationModelUtilities.GetEdgeKey(edge)))
                        {
                            edges.Add(edge);
                        }
                    }

                    if (visited.Add(callerId))
                    {
                        queue.Enqueue((normalizedCaller, depth + 1));
                    }
                }
            }
            else
            {
                var callees = await CollectCalleesAsync(current, solution, ct).ConfigureAwait(false);
                foreach (var (callee, location) in callees)
                {
                    var normalizedCallee = callee.OriginalDefinition ?? callee;
                    var fromId = SymbolIdentity.CreateId(current.OriginalDefinition ?? current);
                    var toId = SymbolIdentity.CreateId(normalizedCallee);
                    var edge = new CallEdge(fromId, toId, NavigationModelUtilities.CreateSourceLocation(location));
                    if (edgeKeys.Add(NavigationModelUtilities.GetEdgeKey(edge)))
                    {
                        edges.Add(edge);
                    }

                    if (visited.Add(toId))
                    {
                        queue.Enqueue((normalizedCallee, depth + 1));
                    }
                }
            }
        }

        return edges.OrderBy(static edge => edge, CallEdgeComparer.Instance).ToArray();
    }

    private static async Task<IReadOnlyList<(ISymbol Symbol, Location Location)>> CollectCalleesAsync(ISymbol symbol,
        Solution solution,
        CancellationToken ct)
    {
        var results = new List<(ISymbol, Location)>();

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            ct.ThrowIfCancellationRequested();
            var node = await reference.GetSyntaxAsync(ct).ConfigureAwait(false);
            var document = solution.GetDocument(node.SyntaxTree);
            if (document == null)
            {
                continue;
            }

            var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (semanticModel == null)
            {
                continue;
            }

            var collector = new CalleeCollector(semanticModel, ct);
            collector.Visit(node);
            results.AddRange(collector.Callees);
        }

        return results;
    }

    private sealed class CalleeCollector : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly CancellationToken _cancellationToken;
        private readonly List<(ISymbol Symbol, Location Location)> _callees = new();

        public CalleeCollector(SemanticModel semanticModel, CancellationToken cancellationToken)
            : base(SyntaxWalkerDepth.Node)
        {
            _semanticModel = semanticModel;
            _cancellationToken = cancellationToken;
        }

        public IReadOnlyList<(ISymbol Symbol, Location Location)> Callees => _callees;

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            RecordSymbol(node.Expression, node.GetLocation());
            base.VisitInvocationExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            RecordSymbol(node, node.GetLocation());
            base.VisitObjectCreationExpression(node);
        }

        private void RecordSymbol(ExpressionSyntax expression, Location location)
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var info = _semanticModel.GetSymbolInfo(expression, _cancellationToken);
            var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
            if (symbol == null || !location.IsInSource)
            {
                return;
            }

            _callees.Add((symbol.OriginalDefinition ?? symbol, location));
        }
    }
}
