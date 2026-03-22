using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace RoslynMcp.Tools.Inspection.TraceCallFlow;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddTraceCallFlowTool() => services
            .AddSingleton<Service>()
            .AddSingleton<Tool>();
    }

    internal static (string Direction, ErrorInfo? Error) NormalizeDirection(this string? direction)
    {
        var normalized = string.IsNullOrWhiteSpace(direction) ? FlowDirections.Both : direction.Trim().ToLowerInvariant();
        return normalized switch
        {
            "upstream" or "up" => (FlowDirections.Upstream, null),
            "downstream" or "down" => (FlowDirections.Downstream, null),
            "both" => (FlowDirections.Both, null),
            _ => (FlowDirections.Both, new ErrorInfo("invalid_input", "direction must be one of: upstream, downstream, both.", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["field"] = "direction",
                ["provided"] = direction ?? string.Empty,
                ["expected"] = "upstream|downstream|both"
            }))
        };
    }

    extension(ISymbol symbol)
    {
        internal TraceRootSummary ToRootSummary()
        {
            var (filePath, line, column) = symbol.GetDeclarationPosition();
            return new TraceRootSummary(symbol.Name, symbol.Kind.ToString(), symbol.ContainingType?.Name ?? symbol.ContainingNamespace.NormalizeNamespace(), CreateOptionalSourceLocation(filePath, line, column));
        }

        internal TraceSymbolEntry ToTraceSymbolEntry()
        {
            var (filePath, line, column) = symbol.GetDeclarationPosition();
            return new TraceSymbolEntry(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), CreateOptionalSourceLocation(filePath, line, column));
        }
    }

    internal static async Task<IReadOnlyList<(ISymbol From, ISymbol To, SourceLocation Location)>> GetCallersAsync(this ISymbol root, Solution solution, int maxDepth, CancellationToken cancellationToken)
        => await BuildCallGraphAsync(root, solution, maxDepth, callers: true, cancellationToken).ConfigureAwait(false);

    internal static async Task<IReadOnlyList<(ISymbol From, ISymbol To, SourceLocation Location)>> GetCalleesAsync(this ISymbol root, Solution solution, int maxDepth, CancellationToken cancellationToken)
        => await BuildCallGraphAsync(root, solution, maxDepth, callers: false, cancellationToken).ConfigureAwait(false);

    internal static async Task<IReadOnlyList<(ISymbol From, ISymbol To, SourceLocation Location)>> GetPossibleTargetsAsync(this ISymbol root, Solution solution, CancellationToken cancellationToken)
    {
        var collected = new Dictionary<string, (ISymbol From, ISymbol To, SourceLocation Location)>(StringComparer.Ordinal);

        foreach (var reference in root.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            var document = solution.GetDocument(node.SyntaxTree);
            if (document is null)
                continue;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
                continue;

            foreach (var invocation in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation.Expression, cancellationToken);
                var target = symbolInfo.Symbol as IMethodSymbol;
                if (target is null || target.ContainingType.TypeKind != TypeKind.Interface)
                    continue;

                var implementations = await SymbolFinder.FindImplementationsAsync(target.OriginalDefinition, solution, solution.Projects.ToImmutableHashSet(), cancellationToken).ConfigureAwait(false);
                var source = invocation.GetLocation().ToSourceLocation();
                foreach (var implementation in implementations.OfType<IMethodSymbol>())
                {
                    var normalized = implementation.ConstructedFrom ?? implementation.OriginalDefinition ?? implementation;
                    collected[$"{root.ToStableId()}->{normalized.ToStableId()}@{source.FilePath}:{source.Line}:{source.Column}"] = (root, normalized, source);
                }
            }
        }

        return [.. collected.Values.OrderBy(static edge => edge.Location.FilePath, StringComparer.Ordinal).ThenBy(static edge => edge.Location.Line).ThenBy(static edge => edge.Location.Column).ThenBy(static edge => edge.To.ToStableId(), StringComparer.Ordinal)];
    }

    private static async Task<IReadOnlyList<(ISymbol From, ISymbol To, SourceLocation Location)>> BuildCallGraphAsync(ISymbol root, Solution solution, int maxDepth, bool callers, CancellationToken cancellationToken)
    {
        var resolvedRoot = root.OriginalDefinition ?? root;
        var edges = new List<(ISymbol From, ISymbol To, SourceLocation Location)>();
        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { resolvedRoot.ToStableId() };
        var queue = new Queue<(ISymbol Symbol, int Depth)>();
        queue.Enqueue((resolvedRoot, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, depth) = queue.Dequeue();
            if (depth >= maxDepth)
                continue;

            if (callers)
            {
                var callerInfos = await SymbolFinder.FindCallersAsync(current, solution, cancellationToken).ConfigureAwait(false);
                foreach (var info in callerInfos)
                {
                    var normalizedCalled = info.CalledSymbol.OriginalDefinition ?? info.CalledSymbol;
                    var normalizedCaller = info.CallingSymbol.OriginalDefinition ?? info.CallingSymbol;

                    foreach (var location in info.Locations.Where(static location => location.IsInSource))
                    {
                        var source = location.ToSourceLocation();
                        if (edgeKeys.Add($"{normalizedCaller.ToStableId()}->{normalizedCalled.ToStableId()}@{source.FilePath}:{source.Line}:{source.Column}"))
                            edges.Add((normalizedCaller, normalizedCalled, source));
                    }

                    if (visited.Add(normalizedCaller.ToStableId()))
                        queue.Enqueue((normalizedCaller, depth + 1));
                }
            }
            else
            {
                var callees = await CollectCalleesAsync(current, solution, cancellationToken).ConfigureAwait(false);
                foreach (var (callee, location) in callees)
                {
                    var normalizedCallee = callee.OriginalDefinition ?? callee;
                    var normalizedCurrent = current.OriginalDefinition ?? current;
                    var source = location.ToSourceLocation();
                    if (edgeKeys.Add($"{normalizedCurrent.ToStableId()}->{normalizedCallee.ToStableId()}@{source.FilePath}:{source.Line}:{source.Column}"))
                        edges.Add((normalizedCurrent, normalizedCallee, source));

                    if (visited.Add(normalizedCallee.ToStableId()))
                        queue.Enqueue((normalizedCallee, depth + 1));
                }
            }
        }

        return [.. edges.OrderBy(static edge => edge.Location.FilePath, StringComparer.Ordinal).ThenBy(static edge => edge.Location.Line).ThenBy(static edge => edge.Location.Column)];
    }

    private static async Task<IReadOnlyList<(ISymbol Symbol, Location Location)>> CollectCalleesAsync(ISymbol symbol, Solution solution, CancellationToken cancellationToken)
    {
        var results = new List<(ISymbol, Location)>();
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            var document = solution.GetDocument(node.SyntaxTree);
            if (document is null)
                continue;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
                continue;

            var collector = new CalleeCollector(semanticModel, cancellationToken);
            collector.Visit(node);
            results.AddRange(collector.Callees);
        }

        return results;
    }

    internal static SourceLocation ToSourceLocation(this Location location)
    {
        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        return new SourceLocation(span.Path ?? string.Empty, start.Line + 1, start.Character + 1);
    }

    internal static Result WithWorkspaceRelativePaths(this Result result)
        => result with
        {
            Root = result.Root.WithWorkspaceRelativePaths(),
            Symbols = result.Symbols?.ToDictionary(static pair => pair.Key, static pair => pair.Value.WithWorkspaceRelativePaths(), StringComparer.Ordinal),
            Edges = [.. result.Edges.Select(static edge => edge.WithWorkspaceRelativePaths())],
            Error = result.Error.WithWorkspaceRelativePaths()
        };

    private static TraceRootSummary? WithWorkspaceRelativePaths(this TraceRootSummary? root)
        => root is null ? null : root with { Location = root.Location.WithWorkspaceRelativePaths() };
    private static TraceSymbolEntry WithWorkspaceRelativePaths(this TraceSymbolEntry entry)
        => entry with { Location = entry.Location.WithWorkspaceRelativePaths() };
    private static TraceFlowEdge WithWorkspaceRelativePaths(this TraceFlowEdge edge)
        => edge with { Site = edge.Site with { FilePath = edge.Site.FilePath.ToWorkspaceRelativePathIfPossible() } };
    private static SourceLocation? WithWorkspaceRelativePaths(this SourceLocation? location)
        => location is null ? null : location with { FilePath = location.FilePath.ToWorkspaceRelativePathIfPossible() };
    private static ErrorInfo? WithWorkspaceRelativePaths(this ErrorInfo? error)
        => error;

    private static SourceLocation? CreateOptionalSourceLocation(string filePath, int? line, int? column)
        => string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue ? null : new(filePath, line.Value, column.Value);

    private sealed class CalleeCollector(SemanticModel semanticModel, CancellationToken cancellationToken) : CSharpSyntaxWalker(SyntaxWalkerDepth.Node)
    {
        internal List<(ISymbol Symbol, Location Location)> Callees { get; } = [];

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            Collect(node.Expression, node.GetLocation());
            base.VisitInvocationExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            Collect(node, node.GetLocation());
            base.VisitObjectCreationExpression(node);
        }

        private void Collect(SyntaxNode node, Location location)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
            if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor })
                return;

            if (symbol is not null)
                Callees.Add((symbol, location));
        }
    }
}
