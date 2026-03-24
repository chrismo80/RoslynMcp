namespace RoslynMcp.Tools.Inspection.LoadSolution;

public sealed record Result(
    string? SolutionPath,
    IReadOnlyList<ProjectSummary> Projects,
    DiagnosticsSummary? BaselineDiagnostics,
    ErrorInfo? Error = null);

public sealed record ProjectSummary(
    string Name,
    string? Path);

public sealed record DiagnosticsSummary(
    int ErrorCount,
    int WarningCount,
    int InfoCount);
