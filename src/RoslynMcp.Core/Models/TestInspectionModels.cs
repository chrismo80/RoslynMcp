namespace RoslynMcp.Core.Models;

public static class RunTestOutcomes
{
    public const string Passed = "passed";
    public const string TestFailures = "test_failures";
    public const string BuildFailed = "build_failed";
    public const string InfrastructureError = "infrastructure_error";
    public const string Cancelled = "cancelled";
}

public sealed record RunTestsRequest(string? Target = null, string? Filter = null);

public sealed record RunTestsResult(
    string Outcome,
    int? ExitCode,
    IReadOnlyList<TestFailure> Failures,
    IReadOnlyList<BuildDiagnostic>? BuildDiagnostics = null,
    string? Summary = null,
    ErrorInfo? Error = null);

public sealed record TestFailure(
    string? TestName,
    string? Message,
    string? Expected,
    string? Actual,
    string? File,
    int? Line,
    string? Code,
    string? StackTrace);

public sealed record BuildDiagnostic(
    string? Id,
    string? Message,
    string? File,
    int? Line,
    int? Column,
    string? Severity);
