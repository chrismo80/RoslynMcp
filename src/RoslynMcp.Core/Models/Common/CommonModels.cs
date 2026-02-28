namespace RoslynMcp.Core.Models.Common;

public sealed record SourceLocation(string FilePath, int Line, int Column);

public sealed record ErrorInfo(string Code, string Message, IReadOnlyDictionary<string, string>? Details = null);

public sealed record DiscoverSolutionsRequest(string WorkspaceRoot);

public sealed record DiscoverSolutionsResult(IReadOnlyList<string> SolutionPaths, ErrorInfo? Error = null);

public sealed record SelectSolutionRequest(string SolutionPath);

public sealed record SelectSolutionResult(string? SelectedSolutionPath, ErrorInfo? Error = null);

public sealed record ReloadSolutionRequest();

public sealed record ReloadSolutionResult(bool Success, ErrorInfo? Error = null);
