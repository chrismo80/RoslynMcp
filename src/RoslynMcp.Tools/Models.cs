using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools;

public sealed record ErrorInfo(
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);

public sealed record TypeSymbol(
	string SymbolId,
	string? Location,
	string DisplayName,
	string Kind)
{
	internal string Text => $"{SymbolId}: {Kind} {DisplayName}";
	
	public static TypeSymbol From(INamedTypeSymbol symbol, SymbolManager symbolManager, WorkspaceManager workspaceManager)
	{
		return new TypeSymbol(
			symbolManager.ToId(symbol),
			symbol.GetLocation(workspaceManager),
			symbol.Name,
			symbol.ToTypeKind()
		);
	}
}

public sealed record MemberSymbol(
	string SymbolId,
	string? Location,
	string DisplayName,
	string? Kind,
	string Accessibility)
{
	internal string Text => $"{SymbolId}: {Accessibility} {DisplayName}";
	
	public static MemberSymbol From(ISymbol symbol, SymbolManager symbolManager, WorkspaceManager workspaceManager)
	{
		return new MemberSymbol(
			symbolManager.ToId(symbol),
			symbol.GetLocation(workspaceManager),
			symbol.ToLightweightMemberSignature(),
			symbol.ToMemberKind(),
			symbol.DeclaredAccessibility.ToText()
		);
	}
}