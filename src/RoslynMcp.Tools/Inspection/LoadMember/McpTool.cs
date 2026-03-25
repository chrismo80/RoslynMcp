using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadMember;

public sealed record Result(
    MemberSymbol? Symbol,
    SymbolDocumentation? Documentation,
    IReadOnlyList<MemberSymbol> References,
    IReadOnlyList<MemberSymbol> Callers,
    IReadOnlyList<MemberSymbol> Callees,
    IReadOnlyList<MemberSymbol> Overrides,
    IReadOnlyList<MemberSymbol> Implementations,
    ErrorInfo? Error = null);

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager
    ) : Tool
{
    [McpServerTool(Name = "load_member", Title = "Load Member", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need callers/calles or overrides/implementations of a symbol.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("The stable symbol ID, obtained from load_type.")]
        string? symbolId = null
        )
    {
        if (solutionManager.Solution is not { } solution)
            return new Result(null, null, [], [], [], [], [], new ErrorInfo("load solution first"));
        
        if (symbolManager.ToSymbol(symbolId) is not ISymbol symbol)
            return new Result(null, null, [], [], [], [], [], new ErrorInfo("symbol not found"));
        
        var references = await SymbolFinder.FindReferencesAsync(symbol, solutionManager.Solution, cancellationToken)
            .ConfigureAwait(false);

        var callers = await SymbolFinder.FindCallersAsync(symbol, solutionManager.Solution, cancellationToken)
            .ConfigureAwait(false);

        var callees = await CollectCalleesAsync(symbol, solution, cancellationToken);
        
        var overrides = await SymbolFinder.FindOverridesAsync(symbol, solutionManager.Solution, null, cancellationToken)
            .ConfigureAwait(false);
        
        var implementations = await SymbolFinder.FindImplementedInterfaceMembersAsync(symbol, solutionManager.Solution, null, cancellationToken)
            .ConfigureAwait(false);

        var usages = callers.Select(c => MemberSymbol.From(c.CallingSymbol, symbolManager, workspaceManager))
            .Concat(references.Where(r => r.Definition != symbol).Select(r => MemberSymbol.From(r.Definition, symbolManager, workspaceManager)))
            .Concat(overrides.Select(o => MemberSymbol.From(o, symbolManager, workspaceManager)))
            .Concat(implementations.Select(i => MemberSymbol.From(i, symbolManager, workspaceManager)))
            .ToList();

        var documentation = symbol.GetDocumentation();
        
        return new Result(
            MemberSymbol.From(symbol, symbolManager, workspaceManager),
            documentation,
            references.Where(r => r.Definition != symbol).Select(r => MemberSymbol.From(r.Definition, symbolManager, workspaceManager)).ToList(),
            callers.Select(c => MemberSymbol.From(c.CallingSymbol, symbolManager, workspaceManager)).ToList(),
            callees.Select(c => MemberSymbol.From(c.Symbol, symbolManager, workspaceManager)).ToList(),
            overrides.Select(o => MemberSymbol.From(o, symbolManager, workspaceManager)).ToList(),
            implementations.Select(i => MemberSymbol.From(i, symbolManager, workspaceManager)).ToList()
            );
    }
    
    private static async Task<IReadOnlyList<(ISymbol Symbol, Location Location)>> CollectCalleesAsync(ISymbol symbol, Solution solution, CancellationToken ct)
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
        private readonly List<(ISymbol Symbol, Location Location)> _callees = [];

        internal CalleeCollector(SemanticModel semanticModel, CancellationToken cancellationToken)
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

        private void RecordSymbol(ExpressionSyntax expression, Microsoft.CodeAnalysis.Location location)
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var info = ModelExtensions.GetSymbolInfo(_semanticModel, expression, _cancellationToken);
            var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
            if (symbol == null || !location.IsInSource)
            {
                return;
            }

            _callees.Add((symbol.OriginalDefinition ?? symbol, location));
        }
    }

}