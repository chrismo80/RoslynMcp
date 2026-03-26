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
    IReadOnlyList<MemberSymbol> Callers,
    IReadOnlyList<MemberSymbol> Callees,
    IReadOnlyList<MemberSymbol> Overrides,
    IReadOnlyList<MemberSymbol> Implementations,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(null, null, [], [], [], [], new ErrorInfo(message, details));
}

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager
    ) : Tool
{
    [McpServerTool(Name = "load_member", Title = "Load Member", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need callers/callees or overrides/implementations of a symbol.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("The stable symbol ID, obtained from load_type.")]
        string? memberSymbolId = null
        )
    {
        if (solutionManager.Solution is not { } solution)
            return Result.AsError("load solution first");

        var symbol = symbolManager.ToSymbol(memberSymbolId);

        if (symbol is null)
            return Result.AsError("symbol not found");

        if (symbol is ITypeSymbol)
            return Result.AsError("no type symbol please");

        try
        {
            return new Result(
                MemberSymbol.From(symbol, symbolManager, workspaceManager),
                symbol.GetDocumentation(),
                await LoadCallers(symbol, solution, cancellationToken),
                await LoadCallees(symbol, solution, cancellationToken),
                await LoadOverrides(symbol, solution, cancellationToken),
                await LoadImplementations(symbol, solution, cancellationToken));
        }
        catch (Exception e)
        {
            return Result.AsError(e.Message, new Dictionary<string, string>
            {
                ["inner exception"] = e.InnerException?.Message ?? string.Empty,
                ["stack trace"] = e.StackTrace ?? string.Empty
            });
        }
    }

    private async Task<IReadOnlyList<MemberSymbol>> LoadCallers(ISymbol symbol, Solution solution, CancellationToken cancellationToken)
    {
        var callers = await SymbolFinder.FindCallersAsync(symbol, solution, cancellationToken).ConfigureAwait(false);

        return callers
            .Select(caller => caller.CallingSymbol)
            .Select(ToMemberSymbol)
            .OfType<MemberSymbol>()
            .ToList();
    }

    private async Task<IReadOnlyList<MemberSymbol>> LoadCallees(ISymbol symbol, Solution solution, CancellationToken cancellationToken)
    {
        var callees = await CollectCalleesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);

        return callees
            .Select(callee => callee.Symbol)
            .Select(ToMemberSymbol)
            .OfType<MemberSymbol>()
            .ToList();
    }

    private async Task<IReadOnlyList<MemberSymbol>> LoadOverrides(ISymbol symbol, Solution solution, CancellationToken cancellationToken)
    {
        var overrides = await SymbolFinder.FindOverridesAsync(symbol, solution, null, cancellationToken).ConfigureAwait(false);

        return overrides
            .Select(ToMemberSymbol)
            .OfType<MemberSymbol>()
            .ToList();
    }

    private async Task<IReadOnlyList<MemberSymbol>> LoadImplementations(ISymbol symbol, Solution solution, CancellationToken cancellationToken)
    {
        var implementations = await SymbolFinder.FindImplementationsAsync(symbol, solution, null, cancellationToken).ConfigureAwait(false);

        return implementations
            .Select(ToMemberSymbol)
            .OfType<MemberSymbol>()
            .ToList();
    }

    private MemberSymbol? ToMemberSymbol(ISymbol symbol)
    {
        var member = MemberSymbol.From(symbol, symbolManager, workspaceManager);

        return member.Kind is null || member.Location.IsNullOrEmpty() ? null : member;
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
                return;

            var info = ModelExtensions.GetSymbolInfo(_semanticModel, expression, _cancellationToken);
            
            var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
            
            if (symbol == null || !location.IsInSource)
                return;

            _callees.Add((symbol.OriginalDefinition ?? symbol, location));
        }
    }
}
