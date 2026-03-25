using System.ComponentModel;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadSymbol;

public sealed record Result(
    MemberSymbol? Root,
    IReadOnlyList<MemberSymbol> Callers,
    IReadOnlyList<MemberSymbol> Implementations,
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
        var symbol = symbolManager.ToSymbol(symbolId);

        var callers = await SymbolFinder.FindCallersAsync(symbol, solutionManager.Solution, cancellationToken)
            .ConfigureAwait(false);

        var implementations = await SymbolFinder.FindImplementationsAsync(symbol, solutionManager.Solution, null, cancellationToken)
            .ConfigureAwait(false);

        return new Result(
            MemberSymbol.From(symbol, symbolManager, workspaceManager),
            callers.Select(call => MemberSymbol.From(call.CallingSymbol, symbolManager, workspaceManager)).ToList(),
            implementations.Select(s => MemberSymbol.From(s, symbolManager, workspaceManager)).ToList());
    }
}