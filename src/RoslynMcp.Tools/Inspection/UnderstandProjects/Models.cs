namespace RoslynMcp.Tools.Inspection.UnderstandProjects;

public sealed record Result(
    IReadOnlyList<ProjectSummary> Projects,
    ErrorInfo? Error = null);

public sealed record ProjectSummary(
    string Name,
    string? ProjectPath,
    IReadOnlyList<string> OutgoingDependencyProjectPaths,
    IReadOnlyList<string> IncomingDependencyProjectPaths,
    IReadOnlyList<string> Types);
