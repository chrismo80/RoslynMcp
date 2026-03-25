using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadMember;

public sealed record Result(
    MemberSymbol? Symbol,
    IReadOnlyList<MemberSymbol> Usages,
    ErrorInfo? Error = null);

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager
    ) : Tool
{
    [McpServerTool(Name = "load_symbol", Title = "Load Symbol", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need callers or implementations of a symbol.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("The stable symbol ID.")]
        string? symbolId = null
        )
    {
        if (solutionManager.Solution is not { } solution)
            return new Result(null, [], new ErrorInfo("load solution first"));
        
        if (symbolManager.ToSymbol(symbolId) is not ISymbol symbol)
            return new Result(null, [], new ErrorInfo("symbol not found"));
        
        IEnumerable<SymbolCallerInfo> callers = await SymbolFinder.FindCallersAsync(symbol, solutionManager.Solution, cancellationToken)
            .ConfigureAwait(false);
        
        IEnumerable<ReferencedSymbol> references = await SymbolFinder.FindReferencesAsync(symbol, solutionManager.Solution, cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<ISymbol> overrides = await SymbolFinder.FindOverridesAsync(symbol, solutionManager.Solution, null, cancellationToken)
            .ConfigureAwait(false);
        
        IEnumerable<ISymbol> implementations = await SymbolFinder.FindImplementedInterfaceMembersAsync(symbol, solutionManager.Solution, null, cancellationToken)
            .ConfigureAwait(false);

        var usages = callers.Select(c => MemberSymbol.From(c.CallingSymbol, symbolManager, workspaceManager))
            .Concat(references.Where(r => r.Definition != symbol).Select(r => MemberSymbol.From(r.Definition, symbolManager, workspaceManager)))
            .Concat(overrides.Select(o => MemberSymbol.From(o, symbolManager, workspaceManager)))
            .Concat(implementations.Select(i => MemberSymbol.From(i, symbolManager, workspaceManager)))
            .ToList();
        
        return new Result(MemberSymbol.From(symbol, symbolManager, workspaceManager), usages);
    }
}