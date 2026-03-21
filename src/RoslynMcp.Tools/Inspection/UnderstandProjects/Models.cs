namespace RoslynMcp.Tools.Inspection.UnderstandProjects;

public sealed record Request(string? Profile = null);

public sealed record Result(
	string Profile,
	IReadOnlyList<ProjectSummary> Projects,
	IReadOnlyList<Hotspot> Hotspots,
	ErrorInfo? Error = null);

public sealed record ProjectSummary(
	string Name,
	string? ProjectPath,
	IReadOnlyList<string> OutgoingDependencyProjectPaths,
	IReadOnlyList<string> IncomingDependencyProjectPaths,
	IReadOnlyList<string> Types);

public sealed record Hotspot(
	string Display,
	string Reason,
	int Score,
	string SymbolId,
	SourceLocation? Location,
	int Complexity,
	int LineCount);

public sealed record SourceLocation(string FilePath, int Line, int Column);

public sealed record ErrorInfo(
	string Code,
	string Message,
	IReadOnlyDictionary<string, string>? Details = null);
