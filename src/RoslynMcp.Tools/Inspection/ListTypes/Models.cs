namespace RoslynMcp.Tools.Inspection.ListTypes;

public sealed record Request(
	string? ProjectPath = null,
	string? ProjectName = null,
	string? ProjectId = null,
	string? NamespacePrefix = null,
	string? Kind = null,
	string? Accessibility = null,
	bool IncludeSummary = true,
	bool IncludeMembers = false,
	int? Limit = null,
	int? Offset = null);

public sealed record Result(
	IReadOnlyList<Entry> Types,
	int TotalCount,
	Context Context,
	ErrorInfo? Error = null);

public sealed record Entry(
	string DisplayName,
	string SymbolId,
	SourceLocation? Location,
	string Kind,
	int? Arity,
	string? Summary = null,
	IReadOnlyList<string>? Members = null);

public sealed record SourceLocation(string FilePath, int Line, int Column);

public sealed record Context(
	string SourceBias,
	string Completeness,
	IReadOnlyList<string> Limitations,
	IReadOnlyList<string> DegradedReasons,
	string? RecommendedNextStep = null);

public sealed record ErrorInfo(
	string Code,
	string Message,
	IReadOnlyDictionary<string, string>? Details = null);

internal sealed record Discovery(Entry Entry, Microsoft.CodeAnalysis.INamedTypeSymbol Symbol);

internal sealed record VisibilityAssessment(
	string Visibility,
	int HandwrittenCount,
	int GeneratedCount,
	int UnknownCount)
{
	public bool HasHandwritten => HandwrittenCount > 0;
	public bool HasGenerated => GeneratedCount > 0;
}

internal sealed record SymbolDocumentation(
	string? Summary,
	string? Returns,
	IReadOnlyList<SymbolParameterDocumentation> Parameters);

internal sealed record SymbolParameterDocumentation(
	string Name,
	string Description);

internal static class SourceBiases
{
	public const string Handwritten = "handwritten";
	public const string Generated = "generated";
	public const string Mixed = "mixed";
	public const string Unknown = "unknown";
}

internal static class ResultCompletenessStates
{
	public const string Complete = "complete";
	public const string Partial = "partial";
	public const string Degraded = "degraded";
}
