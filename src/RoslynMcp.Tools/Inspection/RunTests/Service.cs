using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Inspection.RunTests.Internals;

namespace RoslynMcp.Tools.Inspection.RunTests;

public sealed class Service(Workspace workspace)
{
	private readonly ProcessRunner _processRunner = new();
	private readonly ResultInterpreter _resultInterpreter = new();

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
			var processResult = await _processRunner.RunAsync(targetResolution.EffectiveTargetPath!, artifacts.ResultsDirectory, request.Filter, cancellationToken)
				.ConfigureAwait(false);

			var trxReports = DiscoverTrxReports(artifacts.ResultsDirectory);
			return _resultInterpreter.Interpret(processResult, trxReports).WithWorkspaceRelativePaths();
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

	private sealed record TargetResolution(string? EffectiveTargetPath, ErrorInfo? Error);

	private sealed record TestRunArtifacts(string RootDirectory, string ResultsDirectory);
}
