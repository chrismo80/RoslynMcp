using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.FindCallers;

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager
    ) : Tool
{
    [McpServerTool(Name = "find_callers", Title = "Find Callers", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need only the immediate direct upstream callers of a symbol. This is a focused wrapper around call-flow tracing and does not traverse beyond one caller level.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("The stable symbol ID, obtained from resolve_symbol, list_types, or list_members, for the symbol whose immediate direct callers you want to inspect.")]
        string? symbolId = null
        )
    {

        IEnumerable<SymbolCallerInfo> callers = await SymbolFinder.FindCallersAsync(symbolManager.ToSymbol(symbolId), solutionManager.Solution, cancellationToken);
       
        return new Result(null, null, null, 0, null, [], [], [], []);
    }
}