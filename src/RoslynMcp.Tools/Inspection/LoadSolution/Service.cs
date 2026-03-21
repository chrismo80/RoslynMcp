using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcp.Tools.Inspection.LoadSolution;

public sealed class Service : IAsyncDisposable
{
	private static readonly WorkspaceReadiness DefaultReadiness = new(ReadinessStates.Ready, []);
	private static readonly SemaphoreSlim Gate = new(1, 1);
	private static readonly object RegistrationLock = new();
	private static bool _msbuildRegistered;

	private Session? _current;
	private int _workspaceVersion;

	public async Task<Result> LoadAsync(Request request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		var workspaceRoot = GetWorkspaceRoot();
		var hint = request.SolutionHintPath?.ToWorkspaceAbsolutePath(workspaceRoot)?.Trim();

		var (solutionPath, error) = await ResolveSolutionPathAsync(hint, workspaceRoot, cancellationToken).ConfigureAwait(false);

		if (solutionPath is null)
			return Failure(workspaceRoot, DefaultReadiness, error);

		var loaded = await TryLoadSessionAsync(solutionPath, workspaceRoot, cancellationToken).ConfigureAwait(false);

		if (loaded.Error is not null)
			return Failure(workspaceRoot, DefaultReadiness, loaded.Error);

		await ReplaceCurrentSessionAsync(loaded.Session!, cancellationToken).ConfigureAwait(false);

		var projects = loaded.Session!.Solution.Projects
			.OrderBy(static project => project.Name, StringComparer.Ordinal)
			.Select(static project => new ProjectSummary(project.Name, project.FilePath))
			.ToArray();

		var diagnostics = await CollectBaselineDiagnosticsAsync(loaded.Session.Solution, cancellationToken).ConfigureAwait(false);
		var readiness = AssessReadiness(loaded.Session.Solution, diagnostics);
		var snapshotId = _workspaceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

		return new Result(loaded.Session.SelectedSolutionPath, loaded.Session.SelectedSolutionPath, snapshotId, projects, diagnostics.ToDiagnosticsSummary(), readiness)
			.WithWorkspaceRelativePaths(workspaceRoot);
	}

	public async ValueTask DisposeAsync()
	{
		await Gate.WaitAsync().ConfigureAwait(false);

		try
		{
			_current?.Dispose();
			_current = null;
		}
		finally
		{
			Gate.Release();
		}
	}

	private static string GetWorkspaceRoot() => Path.GetFullPath(Directory.GetCurrentDirectory());

	private static async Task<(string? SolutionPath, ErrorInfo? Error)> ResolveSolutionPathAsync(
		string? hint, string workspaceRoot, CancellationToken cancellationToken)
	{
		if (hint.IsExplicitSolutionPath())
		{
			if (!File.Exists(hint))
			{
				return (null, Error(Codes.SolutionNotFound,
					$"Solution '{hint}' does not exist.",
					("solutionPath", hint),
					("nextAction", "Provide a valid .sln or .slnx path and retry load_solution.")));
			}

			return (hint, null);
		}

		var root = string.IsNullOrWhiteSpace(hint) ? workspaceRoot : hint;

		var normalizedRoot = NormalizeDirectory(root);

		if (normalizedRoot.Error is not null)
			return (null, normalizedRoot.Error);

		var discovered = await DiscoverSolutionsAsync(normalizedRoot.Path!, cancellationToken).ConfigureAwait(false);

		if (discovered.Error is not null)
			return (null, discovered.Error);

		if (discovered.SolutionPaths.Count == 0)
		{
			return (null, Error(Codes.SolutionNotFound,
				"No solution files were discovered.",
				("workspaceRoot", normalizedRoot.Path),
				("nextAction", "Provide a solution hint path or run load_solution from a workspace that contains a .sln or .slnx file.")));
		}

		return (discovered.SolutionPaths[0], null);
	}

	private static async Task<(Session? Session, ErrorInfo? Error)> TryLoadSessionAsync(
		string solutionPath, string workspaceRoot, CancellationToken cancellationToken)
	{
		EnsureMsBuildRegistered();

		MSBuildWorkspace? workspace = null;

		try
		{
			workspace = MSBuildWorkspace.Create();
			var solution = await workspace.OpenSolutionAsync(solutionPath, progress: null, cancellationToken: cancellationToken).ConfigureAwait(false);
			return (new Session(workspaceRoot, solutionPath, workspace, solution), null);
		}
		catch (OperationCanceledException)
		{
			workspace?.Dispose();
			throw;
		}
		catch (Exception ex)
		{
			workspace?.Dispose();

			return (null, Error(Codes.InternalError,
				$"Failed to load solution '{solutionPath}': {ex.Message}",
				("solutionPath", solutionPath)));
		}
	}

	private async Task ReplaceCurrentSessionAsync(Session session, CancellationToken cancellationToken)
	{
		await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			var previous = _current;
			_current = session;
			_workspaceVersion++;
			previous?.Dispose();
		}
		finally
		{
			Gate.Release();
		}
	}

	private static async Task<IReadOnlyList<Diagnostic>> CollectBaselineDiagnosticsAsync(Solution solution, CancellationToken cancellationToken)
	{
		var diagnostics = new List<Diagnostic>();

		foreach (var project in solution.Projects)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

			if (compilation is null)
				continue;

			diagnostics.AddRange(compilation.GetDiagnostics(cancellationToken: cancellationToken));
		}

		return diagnostics;
	}

	private static WorkspaceReadiness AssessReadiness(Solution solution, IReadOnlyList<Diagnostic> diagnostics)
	{
		var missingDocuments = solution.Projects
			.SelectMany(static project => project.Documents)
			.Where(static document => !string.IsNullOrWhiteSpace(document.FilePath))
			.Where(static document => !File.Exists(document.FilePath!))
			.ToArray();

		var missingGeneratedDocuments = missingDocuments
			.Where(static document => IsGeneratedLike(document.FilePath))
			.ToArray();

		if (missingDocuments.Length > 0)
		{
			var degradedReasons = new List<string>();

			if (missingGeneratedDocuments.Length > 0)
				degradedReasons.Add("missing_artifacts");

			if (missingDocuments.Length > missingGeneratedDocuments.Length)
				degradedReasons.Add("missing_documents");

			return new WorkspaceReadiness(
				ReadinessStates.DegradedMissingArtifacts,
				degradedReasons,
				"Run dotnet restore/build to regenerate missing artifacts, then reload the solution if discovery looks incomplete.");
		}

		var generatedDiagnostics = diagnostics.Count(static diagnostic =>
			IsGeneratedLike(diagnostic.Location.GetLineSpan().Path)
			&& diagnostic.Severity != DiagnosticSeverity.Hidden
			&& diagnostic.Severity != DiagnosticSeverity.Info);

		if (generatedDiagnostics > 0)
		{
			return new WorkspaceReadiness(ReadinessStates.DegradedRestoreRecommended,
				["generated_or_intermediate_diagnostics"],
				"Navigation may still work, but running dotnet restore/build should improve workspace completeness.");
		}

		return DefaultReadiness;
	}

	private static (string? Path, ErrorInfo? Error) NormalizeDirectory(string? path)
	{
		var root = path?.Trim();

		if (string.IsNullOrWhiteSpace(root))
		{
			return (null, Error(
				Codes.InvalidPath,
				"Workspace root must be provided.",
				("field", "workspaceRoot")));
		}

		try
		{
			var normalized = Path.GetFullPath(root);

			if (!Directory.Exists(normalized))
			{
				return (null, Error(Codes.InvalidPath,
					$"Workspace root '{normalized}' could not be found.",
					("workspaceRoot", normalized)));
			}

			return (normalized, null);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return (null, Error(Codes.InvalidPath,
				$"Workspace root '{root}' is invalid: {ex.Message}",
				("workspaceRoot", root)));
		}
	}

	private static Task<(IReadOnlyList<string> SolutionPaths, ErrorInfo? Error)> DiscoverSolutionsAsync(string normalizedRoot, CancellationToken cancellationToken)
	{
		var solutions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		try
		{
			foreach (var pattern in new[] { "*.sln", "*.slnx" })
			{
				foreach (var solutionPath in Directory.EnumerateFiles(normalizedRoot, pattern, SearchOption.AllDirectories))
				{
					cancellationToken.ThrowIfCancellationRequested();
					solutions.Add(Path.GetFullPath(solutionPath));
				}
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			return Task.FromResult(((IReadOnlyList<string>)Array.Empty<string>(), (ErrorInfo?)Error(
				Codes.InvalidPath,
				$"Failed to read workspace '{normalizedRoot}': {ex.Message}",
				("workspaceRoot", normalizedRoot))));
		}

		var ordered = solutions.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();

		return Task.FromResult(((IReadOnlyList<string>)ordered, (ErrorInfo?)null));
	}

	private static bool IsGeneratedLike(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return false;

		var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

		var fileName = Path.GetFileName(normalized);

		if (normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
			|| normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
			|| fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
			|| fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
			|| fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
			|| fileName.EndsWith(".AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase)
			|| fileName.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
	}

	private static void EnsureMsBuildRegistered()
	{
		if (_msbuildRegistered)
			return;

		lock (RegistrationLock)
		{
			if (_msbuildRegistered)
				return;

			if (!MSBuildLocator.IsRegistered)
				MSBuildLocator.RegisterDefaults();

			_msbuildRegistered = true;
		}
	}

	private static ErrorInfo Error(string code, string message, params (string Key, string? Value)[] details)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var (key, value) in details)
		{
			if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
				map[key] = value;
		}

		return new ErrorInfo(code, message, map.Count == 0 ? null : map);
	}

	private static Result Failure(string workspaceRoot, WorkspaceReadiness readiness, ErrorInfo? error) =>
		new Result(null, string.Empty, string.Empty, [], new DiagnosticsSummary(0, 0, 0, 0), readiness, error);

	private sealed class Session(string workspaceRoot, string selectedSolutionPath, MSBuildWorkspace workspace, Solution solution) : IDisposable
	{
		public string WorkspaceRoot { get; } = workspaceRoot;
		public string SelectedSolutionPath { get; } = selectedSolutionPath;
		public MSBuildWorkspace Workspace { get; } = workspace;
		public Solution Solution { get; } = solution;

		public void Dispose() => Workspace.Dispose();
	}

	private static class Codes
	{
		public const string InvalidPath = "invalid_path";
		public const string SolutionNotFound = "solution_not_found";
		public const string InternalError = "internal_error";
	}
}