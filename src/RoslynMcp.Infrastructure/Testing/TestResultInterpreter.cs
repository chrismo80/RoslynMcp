using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Infrastructure.Testing;

internal interface ITestResultInterpreter
{
    RunTestsResult Interpret(TestProcessResult processResult, IReadOnlyList<string> jsonReportPaths, IReadOnlyList<string> trxReportPaths);
}

internal sealed partial class TestResultInterpreter : ITestResultInterpreter
{
    public RunTestsResult Interpret(TestProcessResult processResult, IReadOnlyList<string> jsonReportPaths, IReadOnlyList<string> trxReportPaths)
    {
        var jsonFailures = ParseJsonFailures(jsonReportPaths);
        if (jsonFailures.Count > 0)
        {
            return new RunTestsResult(
                RunTestOutcomes.TestFailures,
                processResult.ExitCode,
                jsonFailures,
                Summary: BuildFailureSummary(jsonFailures.Count));
        }

        var trxFailures = ParseTrxFailures(trxReportPaths);
        if (trxFailures.Count > 0)
        {
            return new RunTestsResult(
                RunTestOutcomes.TestFailures,
                processResult.ExitCode,
                trxFailures,
                Summary: BuildFailureSummary(trxFailures.Count));
        }

        if (TryGetNoTestsMatchedSummary(processResult, trxReportPaths, out var noTestsMatchedSummary))
        {
            return new RunTestsResult(
                RunTestOutcomes.Passed,
                processResult.ExitCode,
                Array.Empty<TestFailure>(),
                Summary: noTestsMatchedSummary);
        }

        if (processResult.ExitCode == 0)
        {
            return new RunTestsResult(
                RunTestOutcomes.Passed,
                processResult.ExitCode,
                Array.Empty<TestFailure>(),
                Summary: "All tests passed.");
        }

        var diagnostics = ParseBuildDiagnostics(processResult.StandardOutput, processResult.StandardError);
        if (diagnostics.Count > 0)
        {
            return new RunTestsResult(
                RunTestOutcomes.BuildFailed,
                processResult.ExitCode,
                Array.Empty<TestFailure>(),
                diagnostics,
                Summary: diagnostics[0].Message);
        }

        if (TryGetInfrastructureFailureSummary(processResult.StandardOutput, processResult.StandardError, out var infrastructureSummary))
        {
            return new RunTestsResult(
                RunTestOutcomes.InfrastructureError,
                processResult.ExitCode,
                Array.Empty<TestFailure>(),
                Summary: infrastructureSummary);
        }

        return new RunTestsResult(
            RunTestOutcomes.BuildFailed,
            processResult.ExitCode,
            Array.Empty<TestFailure>(),
            Summary: "dotnet test failed before reporting test results.");
    }

    private static string BuildFailureSummary(int count)
        => count == 1 ? "1 test failed." : $"{count} tests failed.";

    private static IReadOnlyList<TestFailure> ParseJsonFailures(IReadOnlyList<string> reportPaths)
    {
        var failures = new List<TestFailure>();

        foreach (var reportPath in reportPaths)
        {
            try
            {
                using var stream = File.OpenRead(reportPath);
                using var document = JsonDocument.Parse(stream);
                foreach (var element in EnumerateFailureObjects(document.RootElement))
                {
                    failures.Add(new TestFailure(
                        ReadString(element, "TestName", "Method", "Test", "Name"),
                        ReadString(element, "Message"),
                        ReadValueAsString(element, "Expected"),
                        ReadValueAsString(element, "Actual"),
                        ReadString(element, "File"),
                        ReadInt(element, "Line"),
                        ReadString(element, "Code"),
                        ReadString(element, "StackTrace")));
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        return failures;
    }

    private static IEnumerable<JsonElement> EnumerateFailureObjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (TryGetProperty(root, out var failuresProperty, "Failures", "Results", "TestResults") && failuresProperty.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in failuresProperty.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }

        yield return root;
    }

    private static IReadOnlyList<TestFailure> ParseTrxFailures(IReadOnlyList<string> trxFilePaths)
    {
        if (trxFilePaths.Count == 0)
        {
            return Array.Empty<TestFailure>();
        }

        var failures = new List<TestFailure>();

        foreach (var trxFilePath in trxFilePaths)
        {
            if (!File.Exists(trxFilePath))
            {
                continue;
            }

            try
            {
                var document = XDocument.Load(trxFilePath);
                XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

                failures.AddRange(document.Descendants(ns + "UnitTestResult")
                    .Where(static element => string.Equals((string?)element.Attribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase))
                    .Select(element =>
                    {
                        var stackTrace = element.Element(ns + "Output")?.Element(ns + "ErrorInfo")?.Element(ns + "StackTrace")?.Value;
                        var location = TryParseStackTraceLocation(stackTrace);
                        return new TestFailure(
                            (string?)element.Attribute("testName"),
                            element.Element(ns + "Output")?.Element(ns + "ErrorInfo")?.Element(ns + "Message")?.Value,
                            null,
                            null,
                            location?.File,
                            location?.Line,
                            null,
                            stackTrace);
                    }));
            }
            catch
            {
            }
        }

        return failures;
    }

    private static bool TryGetInfrastructureFailureSummary(string standardOutput, string standardError, out string? summary)
    {
        foreach (var line in EnumerateOutputLines(standardOutput, standardError))
        {
            if (!LooksLikeInfrastructureFailure(line))
            {
                continue;
            }

            summary = line;
            return true;
        }

        summary = null;
        return false;
    }

    private static bool TryGetNoTestsMatchedSummary(TestProcessResult processResult, IReadOnlyList<string> trxReportPaths, out string? summary)
    {
        if (string.IsNullOrWhiteSpace(processResult.AppliedFilter))
        {
            summary = null;
            return false;
        }

        if (TryGetTotalExecutedTests(trxReportPaths, out var totalExecutedTests) && totalExecutedTests == 0)
        {
            summary = "No tests matched the filter.";
            return true;
        }

        foreach (var line in EnumerateOutputLines(processResult.StandardOutput, processResult.StandardError))
        {
            if (!line.Contains("testcase filter", StringComparison.OrdinalIgnoreCase)
                || !line.Contains("no test", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            summary = "No tests matched the filter.";
            return true;
        }

        summary = null;
        return false;
    }

    private static bool TryGetTotalExecutedTests(IReadOnlyList<string> trxFilePaths, out int totalExecutedTests)
    {
        totalExecutedTests = 0;
        var foundCounters = false;

        foreach (var trxFilePath in trxFilePaths)
        {
            if (!File.Exists(trxFilePath))
            {
                continue;
            }

            try
            {
                var document = XDocument.Load(trxFilePath);
                XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                var counters = document.Descendants(ns + "Counters");

                foreach (var counter in counters)
                {
                    if (!int.TryParse((string?)counter.Attribute("total"), out var total))
                    {
                        continue;
                    }

                    totalExecutedTests += total;
                    foundCounters = true;
                }
            }
            catch
            {
            }
        }

        return foundCounters;
    }

    private static IReadOnlyList<BuildDiagnostic> ParseBuildDiagnostics(string standardOutput, string standardError)
    {
        var diagnostics = new List<BuildDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in EnumerateOutputLines(standardOutput, standardError))
        {
            var diagnostic = TryParseBuildDiagnostic(line);
            if (diagnostic is null)
            {
                continue;
            }

            var key = string.Join("|", diagnostic.File, diagnostic.Line, diagnostic.Column, diagnostic.Id, diagnostic.Severity, diagnostic.Message);
            if (seen.Add(key))
            {
                diagnostics.Add(diagnostic);
            }
        }

        return diagnostics;
    }

    private static IEnumerable<string> EnumerateOutputLines(string standardOutput, string standardError)
        => (standardOutput + Environment.NewLine + standardError)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
        {
            return null;
        }

        var match = StackTraceLocationRegex().Match(stackTrace);
        if (!match.Success)
        {
            return null;
        }

        return (match.Groups["file"].Value, int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, params string[] names)
        => TryGetProperty(element, out var value, names)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null => null,
                _ => value.ToString()
            }
            : null;

    private static string? ReadValueAsString(JsonElement element, params string[] names)
        => TryGetProperty(element, out var value, names)
            ? value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => value.GetString(),
                _ => value.ToString()
            }
            : null;

    private static int? ReadInt(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static int? ParseNullableInt(string value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private static bool LooksLikeInfrastructureFailure(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return InfrastructureFailurePatterns().Any(pattern => line.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] InfrastructureFailurePatterns()
        =>
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

    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s(?<severity>error|warning)\s(?<id>[A-Za-z]+\d+):\s(?<message>.+?)(?:\s\[.+\])?$", RegexOptions.IgnoreCase)]
    private static partial Regex DetailedDiagnosticRegex();

    [GeneratedRegex(@"^(?<severity>error|warning)\s(?<id>[A-Za-z]+\d+):\s(?<message>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleDiagnosticRegex();

    [GeneratedRegex(@"\sin\s(?<file>.+):line\s(?<line>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex StackTraceLocationRegex();
}
