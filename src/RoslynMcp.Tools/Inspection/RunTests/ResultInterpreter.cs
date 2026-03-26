using System.Text.RegularExpressions;
using System.Xml.Linq;
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
    string? Outcome,
    int? ExitCode,
    IReadOnlyList<TestFailureGroup> FailureGroups,
    IReadOnlyList<BuildDiagnostic>? BuildDiagnostics = null,
    string? Summary = null,
    ErrorInfo? Error = null,
    TestRunCounts? Counts = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(null, -99, [], [], null, new ErrorInfo(message, details));
}

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

internal static partial class ResultInterpreter
{
    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s(?<severity>error|warning)\s(?<id>[A-Za-z]+\d+):\s(?<message>.+?)(?:\s\[.+\])?$", RegexOptions.IgnoreCase)]
    private static partial Regex DetailedDiagnosticRegex();

    [GeneratedRegex(@"^(?<severity>error|warning)\s(?<id>[A-Za-z]+\d+):\s(?<message>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleDiagnosticRegex();

    [GeneratedRegex(@"\sin\s(?<file>.+):line\s(?<line>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex StackTraceLocationRegex();
    
    public static Result Interpret(ProcessResult processResult, ParsedTrxRun trxRun)
    {
        if (trxRun.FailureGroups.Count > 0)
        {
            return new Result(RunTestOutcomes.TestFailures, processResult.ExitCode, trxRun.FailureGroups, Summary: BuildFailureSummary(trxRun.Counts?.Failed ?? trxRun.TotalFailureCount), Counts: trxRun.Counts);
        }

        if (TryGetNoTestsMatchedSummary(processResult, trxRun, out var noTestsMatchedSummary))
        {
            return new Result(RunTestOutcomes.Passed, processResult.ExitCode, [], Summary: noTestsMatchedSummary, Counts: trxRun.Counts);
        }

        if (processResult.ExitCode == 0)
        {
            return new Result(RunTestOutcomes.Passed, processResult.ExitCode, [], Summary: "All tests passed.", Counts: trxRun.Counts);
        }

        var diagnostics = ParseBuildDiagnostics(processResult.StandardOutput, processResult.StandardError);
        
        if (diagnostics.Count > 0)
        {
            return new Result(RunTestOutcomes.BuildFailed, processResult.ExitCode, [], diagnostics, Summary: diagnostics[0].Message);
        }

        if (TryGetInfrastructureFailureSummary(processResult.StandardOutput, processResult.StandardError, out var infrastructureSummary))
        {
            return new Result(RunTestOutcomes.InfrastructureError, processResult.ExitCode, [], Summary: infrastructureSummary);
        }

        return new Result(RunTestOutcomes.BuildFailed, processResult.ExitCode, [], Summary: "dotnet test failed before reporting test results.");
    }

    private static string BuildFailureSummary(int count) => count == 1 ? "1 test failed." : $"{count} tests failed.";

    internal static ParsedTrxRun ParseTrxRun(IReadOnlyList<string> trxFilePaths, WorkspaceManager workspaceManager)
    {
        if (trxFilePaths.Count == 0)
            return ParsedTrxRun.Empty;

        var failures = new List<ParsedFailure>();
        var counts = new MutableCounts();
        var hasCounts = false;

        foreach (var trxFilePath in trxFilePaths.Where(File.Exists))
        {
            try
            {
                var document = XDocument.Load(trxFilePath);
                var ns = document.Root?.Name.Namespace ?? XNamespace.None;

                var testDefinitions = document.Descendants(ns + "UnitTest")
                    .Select(CreateTestDefinition)
                    .Where(static definition => definition.Id is not null)
                    .ToDictionary(static definition => definition.Id!, StringComparer.OrdinalIgnoreCase);

                var fileHasCounters = TryReadCounters(document, ns, out var fileCounts);
                if (fileHasCounters)
                {
                    counts.Add(fileCounts);
                    hasCounts = true;
                }

                foreach (var element in document.Descendants(ns + "UnitTestResult"))
                {
                    var testCase = CreateTestCaseResult(element, ns, testDefinitions);

                    if (!fileHasCounters)
                    {
                        counts.Add(testCase.Outcome);
                        hasCounts = true;
                    }

                    if (string.Equals(testCase.Outcome, "Failed", StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add(new ParsedFailure(
                            testCase.TestName,
                            testCase.Message,
                            workspaceManager.ToRelativePathIfPossible(testCase.File),
                            testCase.Line));
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        return new ParsedTrxRun(BuildFailureGroups(failures), failures.Count, hasCounts ? counts.ToImmutable() : null);
    }

    private static IReadOnlyList<TestFailureGroup> BuildFailureGroups(IReadOnlyList<ParsedFailure> failures)
    {
        if (failures.Count == 0)
            return [];

        return failures
            .GroupBy(static failure => failure.File, GetPathStringComparer())
            .Select(static group => new TestFailureGroup(
                group.Key,
                group.Count(),
                group
                    .Select(static failure => new GroupedTestFailure(failure.TestName, failure.Message, failure.Line))
                    .OrderBy(static failure => failure.Line.HasValue ? 0 : 1)
                    .ThenBy(static failure => failure.Line)
                    .ThenBy(static failure => failure.TestName, StringComparer.Ordinal)
                    .ToArray()))
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.File, GetPathStringComparer())
            .ToArray();
    }

    private static bool TryGetInfrastructureFailureSummary(string standardOutput, string standardError, out string? summary)
    {
        summary = null;
        
        foreach (var line in EnumerateOutputLines(standardOutput, standardError).Where(LooksLikeInfrastructureFailure))
            summary = line;

        return summary is not null;
    }

    private static bool TryGetNoTestsMatchedSummary(ProcessResult processResult, ParsedTrxRun trxRun, out string? summary)
    {
        summary = null;

        if (trxRun.Counts is { Total: 0 })
            summary = "No tests matched the filter.";

        if (EnumerateOutputLines(processResult.StandardOutput, processResult.StandardError).Any(l => l.Contains("testcase filter", StringComparison.OrdinalIgnoreCase) && l.Contains("no test", StringComparison.OrdinalIgnoreCase)))
            summary = "No tests matched the filter.";

        return summary is not null;
    }

    private static IReadOnlyList<BuildDiagnostic> ParseBuildDiagnostics(string standardOutput, string standardError)
    {
        var diagnostics = new List<BuildDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in EnumerateOutputLines(standardOutput, standardError))
        {
            var diagnostic = TryParseBuildDiagnostic(line);
            
            if (diagnostic is null)
                continue;

            var key = string.Join("|", diagnostic.File, diagnostic.Line, diagnostic.Column, diagnostic.Id, diagnostic.Severity, diagnostic.Message);

            if (seen.Add(key))
                diagnostics.Add(diagnostic);
        }

        return diagnostics;
    }

    private static IEnumerable<string> EnumerateOutputLines(string standardOutput, string standardError)
        => (standardOutput + Environment.NewLine + standardError).Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static BuildDiagnostic? TryParseBuildDiagnostic(string line)
    {
        var detailedMatch = DetailedDiagnosticRegex().Match(line);
        
        if (detailedMatch.Success)
        {
            return new BuildDiagnostic(
                detailedMatch.Groups["id"].Value,
                detailedMatch.Groups["message"].Value.Trim(),
                detailedMatch.Groups["file"].Value,
                ParseNullableInt(detailedMatch.Groups["line"].Value),
                ParseNullableInt(detailedMatch.Groups["column"].Value),
                detailedMatch.Groups["severity"].Value.ToLowerInvariant());
        }

        var simpleMatch = SimpleDiagnosticRegex().Match(line);
        
        if (simpleMatch.Success)
        {
            return new BuildDiagnostic(
                simpleMatch.Groups["id"].Value,
                simpleMatch.Groups["message"].Value.Trim(),
                null,
                null,
                null,
                simpleMatch.Groups["severity"].Value.ToLowerInvariant());
        }

        return null;
    }

    private static (string File, int Line)? TryParseStackTraceLocation(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
            return null;

        var match = StackTraceLocationRegex().Match(stackTrace);

        if (!match.Success)
            return null;

        return (match.Groups["file"].Value, int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }


    private static TestDefinition CreateTestDefinition(XElement element) => new(
        (string?)element.Attribute("id"),
        (string?)element.Attribute("name"),
        (string?)element.Element(element.Name.Namespace + "TestMethod")?.Attribute("name"));

    private static ParsedTestCase CreateTestCaseResult(XElement element, XNamespace ns, IReadOnlyDictionary<string, TestDefinition> testDefinitions)
    {
        var outcome = (string?)element.Attribute("outcome") ?? "Unknown";
        var stackTrace = element.Element(ns + "Output")?.Element(ns + "ErrorInfo")?.Element(ns + "StackTrace")?.Value;
        var location = TryParseStackTraceLocation(stackTrace);
        var testId = (string?)element.Attribute("testId");
        testDefinitions.TryGetValue(testId ?? string.Empty, out var definition);

        return new ParsedTestCase(
            (string?)element.Attribute("testName") ?? definition?.Name ?? definition?.MethodName,
            outcome,
            element.Element(ns + "Output")?.Element(ns + "ErrorInfo")?.Element(ns + "Message")?.Value,
            location?.File,
            location?.Line);
    }

    private static bool TryReadCounters(XDocument document, XNamespace ns, out TestRunCounts counts)
    {
        var aggregated = new MutableCounts();
        var found = false;

        foreach (var element in document.Descendants(ns + "Counters"))
        {
            if (!int.TryParse((string?)element.Attribute("total"), out var total))
                continue;

            found = true;
            
            aggregated.Add(new TestRunCounts(
                total,
                ParseNullableInt((string?)element.Attribute("executed")) ?? 0,
                ParseNullableInt((string?)element.Attribute("passed")) ?? 0,
                ParseNullableInt((string?)element.Attribute("failed")) ?? 0,
                (ParseNullableInt((string?)element.Attribute("notExecuted")) ?? 0)
                + (ParseNullableInt((string?)element.Attribute("notRunnable")) ?? 0)
                + (ParseNullableInt((string?)element.Attribute("disconnected")) ?? 0)
                + (ParseNullableInt((string?)element.Attribute("pending")) ?? 0),
                ParseNullableInt((string?)element.Attribute("notExecuted")) ?? 0));
        }

        counts = aggregated.ToImmutable();
        
        return found;
    }

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private static StringComparer GetPathStringComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool LooksLikeInfrastructureFailure(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        return InfrastructureFailurePatterns().Any(pattern => line.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] InfrastructureFailurePatterns() =>
        [
            "invalid format for testcasefilter",
            "the test case filter is not valid",
            "could not find testhost",
            "could not find test host",
            "testhost",
            "test host",
            "failed to initialize",
            "failed to start",
            "process terminated",
            "the active test run was aborted"
        ];
    
    internal sealed record ParsedTrxRun(
        IReadOnlyList<TestFailureGroup> FailureGroups,
        int TotalFailureCount,
        TestRunCounts? Counts)
    {
        public static ParsedTrxRun Empty { get; } = new(Array.Empty<TestFailureGroup>(), 0, null);
    }

    private sealed record ParsedFailure(
        string? TestName,
        string? Message,
        string? File,
        int? Line);

    private sealed record TestDefinition(string? Id, string? Name, string? MethodName);

    private sealed record ParsedTestCase(
        string? TestName,
        string Outcome,
        string? Message,
        string? File,
        int? Line);

    private sealed class MutableCounts
    {
        public int Total { get; private set; }

        public int Executed { get; private set; }

        public int Passed { get; private set; }

        public int Failed { get; private set; }

        public int Skipped { get; private set; }

        public int NotExecuted { get; private set; }

        public void Add(TestRunCounts counts)
        {
            Total += counts.Total;
            Executed += counts.Executed;
            Passed += counts.Passed;
            Failed += counts.Failed;
            Skipped += counts.Skipped;
            NotExecuted += counts.NotExecuted;
        }

        public void Add(string outcome)
        {
            Total++;

            if (IsNotExecutedOutcome(outcome))
            {
                Skipped++;
                NotExecuted++;
                return;
            }

            Executed++;
            if (string.Equals(outcome, "Passed", StringComparison.OrdinalIgnoreCase))
            {
                Passed++;
                return;
            }

            if (string.Equals(outcome, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                Failed++;
            }
        }

        public TestRunCounts ToImmutable() => new(Total, Executed, Passed, Failed, Skipped, NotExecuted);

        private static bool IsNotExecutedOutcome(string outcome)
            => string.Equals(outcome, "NotExecuted", StringComparison.OrdinalIgnoreCase)
               || string.Equals(outcome, "NotRunnable", StringComparison.OrdinalIgnoreCase)
               || string.Equals(outcome, "Disconnected", StringComparison.OrdinalIgnoreCase)
               || string.Equals(outcome, "Pending", StringComparison.OrdinalIgnoreCase);
    }
}
