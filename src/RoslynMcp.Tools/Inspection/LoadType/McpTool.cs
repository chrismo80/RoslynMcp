using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadType;

public sealed record Result(
    IReadOnlyList<MemberSymbol> Members,
    int Count,
    ErrorInfo? Error = null);

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager
    ) : Tool
{
    [McpServerTool(Name = "list_members", Title = "List Members", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to inspect the members declared by a specific type. It returns methods, properties, fields, events, and constructors")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("The stable symbol ID of a type, obtained from list_types.")]
        string? typeSymbolId = null)
    {
        if (symbolManager.ToSymbol(typeSymbolId) is not INamedTypeSymbol symbol)
            return new Result([], 0, new ErrorInfo("type not found"));

        var members = symbol.GetMembers()
            .Select(symbol => MemberSymbol.From(symbol, symbolManager)).ToList();

        return new Result(members,  members.Count);
    }
}