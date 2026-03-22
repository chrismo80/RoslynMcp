namespace RoslynMcp.Tools.Inspection.ResolveSymbols;

public sealed record Request(IReadOnlyList<Entry> Entries);

public sealed record Entry(
	string? SymbolId = null,
	string? Path = null,
	int? Line = null,
	int? Column = null,
	string? QualifiedName = null,
	string? ProjectPath = null,
	string? ProjectName = null,
	string? ProjectId = null,
	string? Label = null);

public sealed record Result(
	IReadOnlyList<ItemResult> Results,
	int TotalCount,
	int ResolvedCount,
	int AmbiguousCount,
	int ErrorCount,
	ErrorInfo? Error = null);

public sealed record ItemResult(
	int Index,
	string? Label,
	ResolvedSymbol? Symbol,
	bool IsAmbiguous,
	IReadOnlyList<Candidate> Candidates,
	ErrorInfo? Error = null);

public sealed record ResolvedSymbol(
	string SymbolId,
	string DisplayName,
	string Kind,
	SourceLocation? Location,
	string? QualifiedDisplayName = null);

public sealed record Candidate(
	string SymbolId,
	string DisplayName,
	string Kind,
	SourceLocation? Location,
	string ProjectName,
	string? QualifiedDisplayName = null);

public sealed record SourceLocation(string FilePath, int Line, int Column);

public sealed record ErrorInfo(
	string Code,
	string Message,
	IReadOnlyDictionary<string, string>? Details = null);
