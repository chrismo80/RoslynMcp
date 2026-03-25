using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.RunTests;

public static class RunTestOutcomes
{
    public const string Passed = "passed";
    public const string TestFailures = "test_failures";
    public const string BuildFailed = "build_failed";
    public const string InfrastructureError = "infrastructure_error";
}

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

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager
    ) : Tool
{
    [McpServerTool(Name = "run_tests", Title = "Run Tests", ReadOnly = true, Idempotent = true)]
    [Description("Default .NET test runner. Use this instead of 'dotnet test' unless you need unsupported CLI behavior.")]
    public async Task<Result> Execute(
        CancellationToken cancellationToken,
        [Description("Optional execution target. Omit to run the currently loaded solution. Supports solution-relative or absolute .sln, .slnx, .csproj, or directory paths when the resolved target stays within the loaded solution directory.")]
        string? target = null,
        [Description("Optional dotnet test filter expression. Passed through to --filter semantics where practical.")]
        string? filter = null)
    {
        if (solutionManager.Solution is not { } solution)
            return new Result(null, -1, [], [], null, new ErrorInfo("load solution first"));

        target = workspaceManager.ToAbsolutePath(target ?? workspaceManager.WorkspaceDirectory);

        var resultsDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcp", Guid.NewGuid().ToString("N"));
        
        Directory.CreateDirectory(resultsDirectory);
        
            try
            {
                var processResult = await TestProcessRunner.RunAsync(target, resultsDirectory, filter, cancellationToken)
                    .ConfigureAwait(false);

                var trxReports = resultsDirectory.DiscoverFiles("*.trx").ToList();

                var trxRun = TestResultInterpreter.ParseTrxRun(trxReports, workspaceManager);
                
                return TestResultInterpreter.Interpret(processResult, trxRun);
            }
            finally
            {
                if (Directory.Exists(resultsDirectory))
                    Directory.Delete(resultsDirectory, recursive: true);
            }
    }
}