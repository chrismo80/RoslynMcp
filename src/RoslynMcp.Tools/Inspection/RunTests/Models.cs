namespace RoslynMcp.Tools.Inspection.RunTests;

public sealed record Request(
    string? Target = null,
    string? Filter = null);

public sealed record Result(
    string Outcome,
    int? ExitCode,
    IReadOnlyList<TestFailureGroup> FailureGroups,
    IReadOnlyList<BuildDiagnostic>? BuildDiagnostics = null,
    string? Summary = null,
    ErrorInfo? Error = null,
    TestRunCounts? Counts = null);

public sealed record TestFailureGroup(
    string? File,
    int Count,
    IReadOnlyList<GroupedTestFailure> Failures);

public sealed record GroupedTestFailure(
    string? TestName,
    string? Message,
    int? Line);

public sealed record TestRunCounts(
    int Total,
    int Executed,
    int Passed,
    int Failed,
    int Skipped,
    int NotExecuted);

public sealed record BuildDiagnostic(
    string? Id,
    string? Message,
    string? File,
    int? Line,
    int? Column,
    string? Severity);

public sealed record ErrorInfo(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);

public static class Outcomes
{
    public const string Passed = "passed";
    public const string TestFailures = "test_failures";
    public const string BuildFailed = "build_failed";
    public const string InfrastructureError = "infrastructure_error";
    public const string Cancelled = "cancelled";
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, string? AppliedFilter);
