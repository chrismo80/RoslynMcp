using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RoslynMcp.Tools.Infrastructure.Services;

namespace RoslynMcp.Tools.Inspection.RunTests;

public sealed partial class Service(Workspace workspace)
{
	public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
		if (session is null)
		{
			return InfrastructureFailure(new ErrorInfo(
				"no_solution_loaded",
				"No solution is currently loaded.",
				new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["nextAction"] = "Call load_solution first to select a solution before running tests."
				}));
		}

		var solutionPath = session.SelectedSolutionPath;
		var targetResolution = ResolveTarget(solutionPath, session.WorkspaceRoot, request.Target);
		if (targetResolution.Error is not null)
			return InvalidInput(targetResolution.Error).WithWorkspaceRelativePaths();

		var artifacts = CreateArtifacts();

		try
		{
			var processResult = await RunProcessAsync(targetResolution.EffectiveTargetPath!, artifacts.ResultsDirectory, request.Filter, cancellationToken)
				.ConfigureAwait(false);

			var trxReports = DiscoverTrxReports(artifacts.ResultsDirectory);
			return Interpret(processResult, trxReports).WithWorkspaceRelativePaths();
		}
		catch (OperationCanceledException)
		{
			return new Result(
				Outcomes.Cancelled,
				null,
				[],
				Summary: "Test execution was cancelled.");
		}
		catch (Exception ex)
		{
			return InfrastructureFailure(new ErrorInfo("internal_error", ex.Message));
		}
		finally
		{
			TryDeleteDirectory(artifacts.RootDirectory);
		}
	}

	private static async Task<ProcessResult> RunProcessAsync(string targetPath, string resultsDirectory, string? filter, CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = "dotnet",
			WorkingDirectory = GetWorkingDirectory(targetPath),
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		AddArguments(startInfo.ArgumentList, targetPath, resultsDirectory, filter);
		PrepareDotnetCliEnvironment(startInfo);

		using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
		try
		{
			process.Start();
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException("Failed to start dotnet test.", ex);
		}

		var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

		try
		{
			await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			TryKillProcess(process);
			throw;
		}

		return new ProcessResult(
			process.ExitCode,
			await standardOutputTask.ConfigureAwait(false),
			await standardErrorTask.ConfigureAwait(false),
			string.IsNullOrWhiteSpace(filter) ? null : filter.Trim());
	}

	private static TargetResolution ResolveTarget(string solutionPath, string workspaceRoot, string? requestedTarget)
	{
		var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
		if (string.IsNullOrWhiteSpace(requestedTarget))
			return new TargetResolution(solutionPath, null);

		var normalizedTarget = Path.GetFullPath(
			Path.IsPathRooted(requestedTarget)
				? requestedTarget
				: Path.Combine(workspaceRoot, requestedTarget.Trim()));

		if (!IsPathWithinRoot(solutionDirectory, normalizedTarget))
		{
			return new TargetResolution(null, new ErrorInfo(
				"invalid_input",
				"Target must be inside the loaded solution directory.",
				new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["field"] = "target",
					["provided"] = requestedTarget
				}));
		}

		if (File.Exists(normalizedTarget))
		{
			var extension = Path.GetExtension(normalizedTarget);
			if (!string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
			{
				return new TargetResolution(null, new ErrorInfo(
					"invalid_input",
					"Target must be a .sln, .slnx, .csproj, or directory path.",
					new Dictionary<string, string>(StringComparer.Ordinal)
					{
						["field"] = "target",
						["provided"] = requestedTarget
					}));
			}

			return new TargetResolution(normalizedTarget, null);
		}

		if (Directory.Exists(normalizedTarget))
			return new TargetResolution(normalizedTarget, null);

		return new TargetResolution(null, new ErrorInfo(
			"invalid_input",
			$"Target '{requestedTarget}' does not exist.",
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["field"] = "target",
				["provided"] = requestedTarget
			}));
	}

	private static IReadOnlyList<string> DiscoverTrxReports(string resultsDirectory)
		=> !Directory.Exists(resultsDirectory)
			? []
			: Directory.EnumerateFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories)
				.OrderBy(static path => path, GetPathStringComparer())
				.ToArray();

	private static TestRunArtifacts CreateArtifacts()
	{
		var rootDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcp", "run-tests", Guid.NewGuid().ToString("N"));
		var resultsDirectory = Path.Combine(rootDirectory, "results");
		Directory.CreateDirectory(resultsDirectory);
		return new TestRunArtifacts(rootDirectory, resultsDirectory);
	}

	private static void AddArguments(Collection<string> argumentList, string targetPath, string resultsDirectory, string? filter)
	{
		argumentList.Add("test");
		argumentList.Add(targetPath);
		argumentList.Add("--nologo");
		argumentList.Add("--verbosity");
		argumentList.Add("minimal");
		argumentList.Add("--logger");
		argumentList.Add("trx");
		argumentList.Add("--results-directory");
		argumentList.Add(resultsDirectory);

		if (!string.IsNullOrWhiteSpace(filter))
		{
			argumentList.Add("--filter");
			argumentList.Add(filter.Trim());
		}
	}

	private static string GetWorkingDirectory(string targetPath)
		=> Directory.Exists(targetPath)
			? targetPath
			: Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();

	private static void PrepareDotnetCliEnvironment(ProcessStartInfo startInfo)
	{
		startInfo.Environment.Remove("MSBuildSDKsPath");
		startInfo.Environment.Remove("MSBUILD_EXE_PATH");
		startInfo.Environment.Remove("MSBuildExtensionsPath");
		startInfo.Environment.Remove("MSBuildLoadMicrosoftTargetsReadOnly");
		startInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR");
	}

	private static void TryKillProcess(Process process)
	{
		try
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
		catch
		{
		}
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
				Directory.Delete(path, recursive: true);
		}
		catch
		{
		}
	}

	private static Result Interpret(ProcessResult processResult, IReadOnlyList<string> trxReportPaths)
	{
		var trxRun = ParseTrxRun(trxReportPaths);

		if (trxRun.FailureGroups.Count > 0)
		{
			return new Result(
				Outcomes.TestFailures,
				processResult.ExitCode,
				trxRun.FailureGroups,
				Summary: BuildFailureSummary(trxRun.Counts?.Failed ?? trxRun.TotalFailureCount),
				Counts: trxRun.Counts);
		}

		if (TryGetNoTestsMatchedSummary(processResult, trxRun, out var noTestsMatchedSummary))
		{
			return new Result(
				Outcomes.Passed,
				processResult.ExitCode,
				[],
				Summary: noTestsMatchedSummary,
				Counts: trxRun.Counts);
		}

		if (processResult.ExitCode == 0)
		{
			return new Result(
				Outcomes.Passed,
				processResult.ExitCode,
				[],
				Summary: "All tests passed.",
				Counts: trxRun.Counts);
		}

		var diagnostics = ParseBuildDiagnostics(processResult.StandardOutput, processResult.StandardError);
		if (diagnostics.Count > 0)
		{
			return new Result(
				Outcomes.BuildFailed,
				processResult.ExitCode,
				[],
				diagnostics,
				Summary: diagnostics[0].Message);
		}

		if (TryGetInfrastructureFailureSummary(processResult.StandardOutput, processResult.StandardError, out var infrastructureSummary))
		{
			return new Result(
				Outcomes.InfrastructureError,
				processResult.ExitCode,
				[],
				Summary: infrastructureSummary);
		}

		return new Result(
			Outcomes.BuildFailed,
			processResult.ExitCode,
			[],
			Summary: "dotnet test failed before reporting test results.");
	}

	private static string BuildFailureSummary(int count)
		=> count == 1 ? "1 test failed." : $"{count} tests failed.";

	private static ParsedTrxRun ParseTrxRun(IReadOnlyList<string> trxFilePaths)
	{
		if (trxFilePaths.Count == 0)
			return ParsedTrxRun.Empty;

		var failures = new List<ParsedFailure>();
		var counts = new MutableCounts();
		var hasCounts = false;

		foreach (var trxFilePath in trxFilePaths)
		{
			if (!File.Exists(trxFilePath))
				continue;

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
						failures.Add(new ParsedFailure(testCase.TestName, testCase.Message, testCase.File, testCase.Line));
					}
				}
			}
			catch
			{
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
				group.Select(static failure => new GroupedTestFailure(failure.TestName, failure.Message, failure.Line))
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
		foreach (var line in EnumerateOutputLines(standardOutput, standardError))
		{
			if (!LooksLikeInfrastructureFailure(line))
				continue;

			summary = line;
			return true;
		}

		summary = null;
		return false;
	}

	private static bool TryGetNoTestsMatchedSummary(ProcessResult processResult, ParsedTrxRun trxRun, out string? summary)
	{
		if (string.IsNullOrWhiteSpace(processResult.AppliedFilter))
		{
			summary = null;
			return false;
		}

		if (trxRun.Counts is { Total: 0 })
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
		=> (standardOutput + Environment.NewLine + standardError)
			.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

	private static TestDefinition CreateTestDefinition(XElement element)
		=> new(
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

	private static bool LooksLikeInfrastructureFailure(string line)
		=> !string.IsNullOrWhiteSpace(line)
			&& InfrastructureFailurePatterns().Any(pattern => line.Contains(pattern, StringComparison.OrdinalIgnoreCase));

	private static StringComparer GetPathStringComparer()
		=> OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	private static StringComparison GetPathStringComparison()
		=> OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	private static bool IsPathWithinRoot(string rootDirectory, string path)
	{
		var normalizedPath = Path.GetFullPath(path);
		var normalizedRoot = Path.GetFullPath(rootDirectory);

		if (string.Equals(normalizedPath, normalizedRoot, GetPathStringComparison()))
			return true;

		return normalizedPath.StartsWith(EnsureTrailingSeparator(normalizedRoot), GetPathStringComparison());
	}

	private static string EnsureTrailingSeparator(string path)
		=> path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
			? path
			: path + Path.DirectorySeparatorChar;

	private static Result InvalidInput(ErrorInfo error)
		=> new(Outcomes.InfrastructureError, null, [], Summary: error.Message, Error: error);

	private static Result InfrastructureFailure(ErrorInfo error)
		=> new(Outcomes.InfrastructureError, null, [], Summary: error.Message, Error: error);

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

	private sealed record TargetResolution(string? EffectiveTargetPath, ErrorInfo? Error);

	private sealed record TestRunArtifacts(string RootDirectory, string ResultsDirectory);

	private sealed record ParsedTrxRun(IReadOnlyList<TestFailureGroup> FailureGroups, int TotalFailureCount, TestRunCounts? Counts)
	{
		public static ParsedTrxRun Empty { get; } = new([], 0, null);
	}

	private sealed record ParsedFailure(string? TestName, string? Message, string? File, int? Line);

	private sealed record TestDefinition(string? Id, string? Name, string? MethodName);

	private sealed record ParsedTestCase(string? TestName, string Outcome, string? Message, string? File, int? Line);

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
				Failed++;
		}

		public TestRunCounts ToImmutable() => new(Total, Executed, Passed, Failed, Skipped, NotExecuted);

		private static bool IsNotExecutedOutcome(string outcome)
			=> string.Equals(outcome, "NotExecuted", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(outcome, "NotRunnable", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(outcome, "Disconnected", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(outcome, "Pending", StringComparison.OrdinalIgnoreCase);
	}
}
