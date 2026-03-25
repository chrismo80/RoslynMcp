using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadType;

public sealed record Result(
    TypeSymbol Symbol,
    IReadOnlyList<TypeSymbol> Derived,
    IReadOnlyList<TypeSymbol> Implementations,
    IReadOnlyList<MemberSymbol> Members,
    ErrorInfo? Error = null);

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager
    ) : Tool
{
    [McpServerTool(Name = "load_type", Title = "Load Type", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to inspect type hierarchy and members declared by the specific type.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("The stable symbol ID of a type, obtained from load_project.")]
        string? typeSymbolId = null)
    {
        if (solutionManager.Solution is not { } solution)
            return new Result(null, [], [], [], new ErrorInfo("load solution first"));
        
        if (symbolManager.ToSymbol(typeSymbolId) is not INamedTypeSymbol symbol)
            return new Result(null, [], [], [], new ErrorInfo("type not found"));

        var deriveClassed = await SymbolFinder.FindDerivedClassesAsync(symbol, solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        
        var derivedInterfaces = await SymbolFinder.FindDerivedInterfacesAsync(symbol, solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        
        var implementations = await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var members = symbol.GetMembers()
            .Select(symbol => MemberSymbol.From(symbol, symbolManager, workspaceManager)).ToList();

        return new Result(TypeSymbol.From(symbol, symbolManager, workspaceManager),
            deriveClassed.Concat(derivedInterfaces).Select(d => TypeSymbol.From(d, symbolManager, workspaceManager)).ToList(),
            implementations.Select(i => TypeSymbol.From(i, symbolManager, workspaceManager)).ToList(),
            members);
    }
}