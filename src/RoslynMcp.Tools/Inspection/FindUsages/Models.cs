namespace RoslynMcp.Tools.Inspection.FindUsages;

public sealed record Request(string SymbolId, string Scope, string? Path = null);

public sealed record Result(
	UsageSymbol? Symbol,
	IReadOnlyList<ReferenceFileGroup> ReferenceFiles,
	int TotalCount,
	ErrorInfo? Error = null);

public sealed record UsageSymbol(string SymbolId, string Display, string Kind, SourceLocation? Location);

public sealed record ReferenceFileGroup(string FilePath, IReadOnlyList<ReferencePosition> References);

public sealed record ReferencePosition(int Line, int Column);

public sealed record SourceLocation(string FilePath, int Line, int Column);

public sealed record ErrorInfo(
	string Code,
	string Message,
	IReadOnlyDictionary<string, string>? Details = null);

internal static class ReferenceScopes
{
	public const string Document = "document";
	public const string Project = "project";
	public const string Solution = "solution";
}
