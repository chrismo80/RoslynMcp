using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.ListMembers;

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

        var members = symbol.GetMembers().Select(ToMemberEntry).ToList();
        
        return new Result(members,  members.Count);
    }
    
    private MemberEntry ToMemberEntry(ISymbol symbol)
    {
        return new MemberEntry(
            symbol.Name,
            symbolManager.ToId(symbol),
            symbol.ToMemberKind() ?? string.Empty,
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            GetDeclarationPosition(symbol),
            symbol.DeclaredAccessibility.ToString(),
            symbol.IsStatic
        );
    }
    
    private Location GetDeclarationPosition(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);

        if (location is null)
            return new Location(string.Empty, 0, 0);

        var span = location.GetLineSpan();
        var start = span.StartLinePosition;

        return new Location(workspaceManager.ToRelativePathIfPossible(span.Path), start.Line + 1, start.Character + 1);
    }
}