using RoslynMcp.Core.Models.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Infrastructure.Analysis;

internal sealed class AnalysisMetricsCollector : IAnalysisMetricsCollector
{
    private readonly IRoslynSymbolIdFactory _symbolIdFactory;

    public AnalysisMetricsCollector(IRoslynSymbolIdFactory symbolIdFactory)
    {
        _symbolIdFactory = symbolIdFactory ?? throw new ArgumentNullException(nameof(symbolIdFactory));
    }

    public Task<IReadOnlyList<MetricItem>> CollectMetricsAsync(Solution solution, string scope, string? path, CancellationToken ct)
        => CollectMetricsAsync(solution.Projects, scope, path, ct);

    public async Task<IReadOnlyList<MetricItem>> CollectMetricsAsync(
        IEnumerable<Project> projects,
        string scope,
        string? path,
        CancellationToken ct)
    {
        var metrics = new List<MetricItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var scopeResolver = new AnalysisScopeResolver();

        foreach (var project in projects.OrderBy(static p => p.FilePath ?? p.Name, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var document in project.Documents.OrderBy(static d => d.FilePath ?? d.Name, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (!scopeResolver.IsDocumentInScope(document, scope, path))
                {
                    continue;
                }

                var syntaxRoot = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (syntaxRoot == null)
                {
                    continue;
                }

                var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                if (semanticModel == null)
                {
                    continue;
                }

                var walker = new MemberMetricWalker(semanticModel, _symbolIdFactory, ct);
                walker.Visit(syntaxRoot);

                foreach (var metric in walker.Metrics)
                {
                    if (seen.Add(metric.SymbolId))
                    {
                        metrics.Add(metric);
                    }
                }
            }
        }

        return metrics;
    }

    private sealed class MemberMetricWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly IRoslynSymbolIdFactory _symbolIdFactory;
        private readonly CancellationToken _cancellationToken;
        private readonly List<MetricItem> _metrics = new();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public MemberMetricWalker(SemanticModel semanticModel, IRoslynSymbolIdFactory symbolIdFactory, CancellationToken cancellationToken)
            : base(SyntaxWalkerDepth.Node)
        {
            _semanticModel = semanticModel;
            _symbolIdFactory = symbolIdFactory;
            _cancellationToken = cancellationToken;
        }

        public IReadOnlyList<MetricItem> Metrics => _metrics;

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            CollectMetric(node);
            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            CollectMetric(node);
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            CollectMetric(node);
            base.VisitLocalFunctionStatement(node);
        }

        private void CollectMetric(SyntaxNode node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var symbol = _semanticModel.GetDeclaredSymbol(node, _cancellationToken) as IMethodSymbol;
            if (symbol == null)
            {
                return;
            }

            if (!symbol.Locations.Any(location => location.IsInSource))
            {
                return;
            }

            var symbolId = _symbolIdFactory.CreateId(symbol);
            if (!_seen.Add(symbolId))
            {
                return;
            }

            int? complexity = null;
            if (HasBody(node))
            {
                var walker = new CyclomaticComplexityWalker(_cancellationToken);
                switch (node)
                {
                    case BaseMethodDeclarationSyntax method:
                        method.Body?.Accept(walker);
                        method.ExpressionBody?.Accept(walker);
                        break;

                    case LocalFunctionStatementSyntax local:
                        local.Body?.Accept(walker);
                        local.ExpressionBody?.Accept(walker);
                        break;
                }

                complexity = walker.Complexity;
            }

            var lineCount = ComputeLineCount(node);
            _metrics.Add(new MetricItem(symbolId, complexity, lineCount));
        }

        private static bool HasBody(SyntaxNode node)
            => node switch
            {
                BaseMethodDeclarationSyntax method => method.Body != null || method.ExpressionBody != null,
                LocalFunctionStatementSyntax local => local.Body != null || local.ExpressionBody != null,
                _ => false
            };

        private static int ComputeLineCount(SyntaxNode node)
        {
            var span = node.GetLocation().GetLineSpan();
            return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        }
    }

    private sealed class CyclomaticComplexityWalker : CSharpSyntaxWalker
    {
        private readonly CancellationToken _cancellationToken;
        private int _count = 1;

        public CyclomaticComplexityWalker(CancellationToken cancellationToken)
            : base(SyntaxWalkerDepth.Node)
        {
            _cancellationToken = cancellationToken;
        }

        public int Complexity => Math.Max(_count, 1);

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _count++;
            base.VisitIfStatement(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _count++;
            base.VisitForStatement(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _count++;
            base.VisitForEachStatement(node);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _count++;
            base.VisitWhileStatement(node);
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _count++;
            base.VisitDoStatement(node);
        }

        public override void VisitSwitchSection(SwitchSectionSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _count += node.Labels.Count;
            base.VisitSwitchSection(node);
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _count++;
            base.VisitConditionalExpression(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
            {
                _count++;
            }

            base.VisitBinaryExpression(node);
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _count++;
            base.VisitCatchClause(node);
        }

        public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _count++;
            base.VisitSwitchExpressionArm(node);
        }

        public override void Visit(SyntaxNode? node)
        {
            if (node == null)
            {
                return;
            }

            _cancellationToken.ThrowIfCancellationRequested();
            base.Visit(node);
        }
    }
}
