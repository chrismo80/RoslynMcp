using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Tools.Infrastructure;

namespace RoslynMcp.Tools.Inspection.UnderstandProjects.Builders;

internal static class HotspotBuilder
{
    public static async Task<IReadOnlyList<Hotspot>> BuildAsync(Solution solution, int hotspotCount, CancellationToken cancellationToken)
    {
        var hotspots = new List<Hotspot>();

        foreach (var project in solution.Projects.OrderBy(static project => project.FilePath ?? project.Name, StringComparer.Ordinal))
        {
            foreach (var document in project.Documents.OrderBy(static document => document.FilePath ?? document.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SourceVisibility.ShouldIncludeInHumanResults(document.FilePath))
                    continue;

                if (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not CSharpSyntaxNode root)
                    continue;

                if (await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not { } semanticModel)
                    continue;

                var collector = new Walker(semanticModel, cancellationToken);
                collector.Visit(root);
                hotspots.AddRange(collector.Hotspots);
            }
        }

        return [.. hotspots
            .OrderByDescending(static hotspot => hotspot.Score)
            .ThenBy(static hotspot => hotspot.SymbolId, StringComparer.Ordinal)
            .Take(hotspotCount)];
    }

    private sealed class Walker(SemanticModel semanticModel, CancellationToken cancellationToken) : CSharpSyntaxWalker(SyntaxWalkerDepth.Node)
    {
        private readonly List<Hotspot> _hotspots = [];

        public IReadOnlyList<Hotspot> Hotspots => _hotspots;

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            Collect(node, semanticModel.GetDeclaredSymbol(node, cancellationToken));
            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            Collect(node, semanticModel.GetDeclaredSymbol(node, cancellationToken));
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            Collect(node, semanticModel.GetDeclaredSymbol(node, cancellationToken));
            base.VisitLocalFunctionStatement(node);
        }

        private void Collect(SyntaxNode node, ISymbol? symbol)
        {
            if (symbol is not IMethodSymbol method || !method.Locations.Any(static location => location.IsInSource))
                return;

            var complexity = ComputeComplexity(node, cancellationToken);
            var lineCount = ComputeLineCount(node);
            var score = complexity + lineCount;
            var span = node.GetLocation().GetLineSpan();
            var location = new SourceLocation(span.Path ?? string.Empty, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);

            _hotspots.Add(new Hotspot(
                method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                $"complexity={complexity}, lines={lineCount}",
                score,
                method.ToStableId(),
                location,
                complexity,
                lineCount));
        }
    }

    private static int ComputeComplexity(SyntaxNode node, CancellationToken cancellationToken)
    {
        var walker = new ComplexityWalker(cancellationToken);
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

        return walker.Complexity;
    }

    private static int ComputeLineCount(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private sealed class ComplexityWalker(CancellationToken cancellationToken) : CSharpSyntaxWalker(SyntaxWalkerDepth.Node)
    {
        private int _count = 1;

        public int Complexity => Math.Max(_count, 1);

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            _count++;
            base.VisitIfStatement(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            _count++;
            base.VisitForStatement(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            _count++;
            base.VisitForEachStatement(node);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            _count++;
            base.VisitWhileStatement(node);
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            _count++;
            base.VisitDoStatement(node);
        }

        public override void VisitSwitchSection(SwitchSectionSyntax node)
        {
            _count += node.Labels.Count;
            base.VisitSwitchSection(node);
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            _count++;
            base.VisitConditionalExpression(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
                _count++;

            base.VisitBinaryExpression(node);
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            _count++;
            base.VisitCatchClause(node);
        }

        public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
        {
            _count++;
            base.VisitSwitchExpressionArm(node);
        }

        public override void Visit(SyntaxNode? node)
        {
            if (node is null)
                return;

            cancellationToken.ThrowIfCancellationRequested();
            base.Visit(node);
        }
    }
}