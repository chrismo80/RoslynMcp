using RoslynMcp.Core.Models.Common;

namespace RoslynMcp.Core.Models.Analysis;

public sealed record AnalyzeSolutionRequest();

public sealed record DiagnosticItem(string Code, string Severity, string Message, SourceLocation Location);

public sealed record AnalyzeSolutionResult(IReadOnlyList<DiagnosticItem> Diagnostics, ErrorInfo? Error = null);

public static class AnalysisScopes
{
    public const string Document = "document";
    public const string Project = "project";
    public const string Solution = "solution";
}

public sealed record AnalyzeScopeRequest(string Scope, string? Path = null);

public sealed record AnalyzeScopeResult(
    string Scope,
    string? Path,
    IReadOnlyList<DiagnosticItem> Diagnostics,
    IReadOnlyList<MetricItem> Metrics,
    ErrorInfo? Error = null);

public sealed record GetCodeMetricsRequest();

public sealed record MetricItem(string SymbolId, int? CyclomaticComplexity, int? LineCount);

public sealed record GetCodeMetricsResult(IReadOnlyList<MetricItem> Metrics, ErrorInfo? Error = null);
