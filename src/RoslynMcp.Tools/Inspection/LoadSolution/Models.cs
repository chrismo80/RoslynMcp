namespace RoslynMcp.Tools.Inspection.LoadSolution;

public sealed record Request(
	string? SolutionHintPath = null);

public sealed record Result(
	string? SelectedSolutionPath,
	string WorkspaceId,
	string WorkspaceSnapshotId,
	IReadOnlyList<ProjectSummary> Projects,
	DiagnosticsSummary BaselineDiagnostics,
	WorkspaceReadiness Readiness,
	ErrorInfo? Error = null);

public sealed record ProjectSummary(
	string Name,
	string? Path);

public sealed record DiagnosticsSummary(
	int ErrorCount,
	int WarningCount,
	int InfoCount,
	int TotalCount);

public sealed record WorkspaceReadiness(
	string State,
	IReadOnlyList<string> DegradedReasons,
	string? RecommendedNextStep = null);

public sealed record ErrorInfo(
	string Code,
	string Message,
	IReadOnlyDictionary<string, string>? Details = null);