using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools;

public sealed record Location(string FilePath, int Line, int Column);

public sealed record ErrorInfo(
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);

public sealed record TypeSymbol(
	INamedTypeSymbol Symbol,
	string Id,
	Location? Location,
	string DisplayName,
	string Kind)
{
	public static TypeSymbol From(INamedTypeSymbol symbol, SymbolManager symbolManager)
	{
		return new TypeSymbol(
			symbol,
			symbolManager.ToId(symbol),
			symbol.GetLocation(),
			symbol.Name,
			symbol.ToTypeKind()
		);
	}

	public string ToLine()
	{
		return $"{Id}: {Kind} {DisplayName}";
	}
}


public sealed record MemberSymbol(
	ISymbol Symbol,
	string SymbolId,
	Location? Location,
	string DisplayName,
	string? Kind,
	string Accessibility,
	bool IsStatic)
{
	public static MemberSymbol From(ISymbol symbol, SymbolManager symbolManager)
	{
		return new MemberSymbol(
			symbol,
			symbolManager.ToId(symbol),
			symbol.GetLocation(),
			symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
			symbol.ToMemberKind(),
			symbol.DeclaredAccessibility.ToText(),
			symbol.IsStatic
		);
	}

	public string ToLine()
	{
		return $"{Accessibility} {Symbol.ToLightweightMemberSignature()}";
	}
}