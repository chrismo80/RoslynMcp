namespace RoslynMcp.Tools.Inspection.ExplainSymbol;

public sealed record Request(string? SymbolId = null, string? Path = null, int? Line = null, int? Column = null);

public sealed record Result(
	CompactSymbol? Symbol,
	string RoleSummary,
	string Signature,
	IReadOnlyList<ReferenceFileGroup>? KeyReferences,
	IReadOnlyList<ImpactHint> ImpactHints,
	SymbolDocumentationInfo? Documentation = null,
	ErrorInfo? Error = null);

public sealed record CompactSymbol(
	string SymbolId,
	string Display,
	string Kind,
	SourceLocation? Location,
	string? Owner = null);

public sealed record ReferencePosition(int Line, int Column);

public sealed record ReferenceFileGroup(string FilePath, IReadOnlyList<ReferencePosition> References);

public sealed record ImpactHint(string Zone, string Reason, int ReferenceCount);

public sealed record SymbolDocumentationParameter(string Name, string Description);

public sealed record SymbolDocumentationInfo(
	string? Summary = null,
	string? Returns = null,
	IReadOnlyList<SymbolDocumentationParameter>? Parameters = null);

public sealed record SourceLocation(string FilePath, int Line, int Column);

public sealed record ErrorInfo(
	string Code,
	string Message,
	IReadOnlyDictionary<string, string>? Details = null);

internal sealed record SymbolDocumentation(string? Summary, string? Returns, IReadOnlyList<SymbolParameterDocumentation> Parameters);

internal sealed record SymbolParameterDocumentation(string Name, string Description);
