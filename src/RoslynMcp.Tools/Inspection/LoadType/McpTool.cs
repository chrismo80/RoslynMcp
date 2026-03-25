using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadType;

public sealed record Result(
    TypeSymbol Symbol,
    IReadOnlyList<TypeSymbol> Usages,
    IReadOnlyList<MemberSymbol> Members,
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
        if (solutionManager.Solution is not { } solution)
            return new Result(null, [], [], new ErrorInfo("load solution first"));
        
        if (symbolManager.ToSymbol(typeSymbolId) is not INamedTypeSymbol symbol)
            return new Result(null, [], [], new ErrorInfo("type not found"));

        var members = symbol.GetMembers()
            .Select(symbol => MemberSymbol.From(symbol, symbolManager, workspaceManager)).ToList();

        IEnumerable<INamedTypeSymbol> deriveClassed = await SymbolFinder.FindDerivedClassesAsync(symbol, solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        
        IEnumerable<INamedTypeSymbol> derivedInterfaces = await SymbolFinder.FindDerivedInterfacesAsync(symbol, solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        
        IEnumerable<INamedTypeSymbol> implementations = await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var usages = deriveClassed.Select(d => TypeSymbol.From(d, symbolManager, workspaceManager))
            .Concat(derivedInterfaces.Select(d => TypeSymbol.From(d, symbolManager, workspaceManager)))
            .Concat(implementations.Select(i => TypeSymbol.From(i, symbolManager, workspaceManager)))
            .ToList();
        
        return new Result(TypeSymbol.From(symbol, symbolManager, workspaceManager), usages, members);
    }
}