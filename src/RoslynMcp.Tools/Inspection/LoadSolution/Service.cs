using Microsoft.CodeAnalysis;
using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Workspace;

namespace RoslynMcp.Tools.Inspection.LoadSolution;

public sealed class Service(
	ISolutionSessionService solutionSessionService,
	IAnalysisService analysisService,
	IRoslynSolutionAccessor solutionAccessor,
	ICurrentWorkspaceRootProvider currentWorkspaceRootProvider)
{
	private static readonly WorkspaceReadiness DefaultReadiness = new(ReadinessStates.Ready, Array.Empty<string>());
    private readonly string _workspaceRoot = currentWorkspaceRootProvider?.WorkspaceRoot ?? throw new ArgumentNullException(nameof(currentWorkspaceRootProvider));

	public async Task<Result> LoadAsync(Request request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		var hint = request.SolutionHintPath?.ToWorkspaceAbsolutePath(_workspaceRoot)?.Trim();
		var (solutionPath, discoveryError) = await ResolveSolutionPathAsync(hint, solutionSessionService, cancellationToken).ConfigureAwait(false);
		if (solutionPath is null)
		{
			return Failure(
				DefaultReadiness,
				discoveryError.ToLocalError("Provide a valid solution path or run load_solution from a folder containing a .sln or .slnx file."));
		}

		var select = await solutionSessionService.SelectSolutionAsync(new SelectSolutionRequest(solutionPath), cancellationToken).ConfigureAwait(false);
		if (select.Error is not null)
		{
			return Failure(
				DefaultReadiness,
				select.Error.ToLocalError("Provide a valid .sln or .slnx path and retry load_solution."));
		}

		var (solution, currentError) = await solutionAccessor.GetCurrentSolutionAsync(cancellationToken).ConfigureAwait(false);
		if (solution is null)
		{
			return new Result(
				select.SelectedSolutionPath,
				string.Empty,
				string.Empty,
				Array.Empty<ProjectSummary>(),
				new DiagnosticsSummary(0, 0, 0, 0),
				DefaultReadiness,
				currentError.ToLocalError("Retry load_solution after the workspace/session is available."))
				.WithWorkspaceRelativePaths(_workspaceRoot);
		}

		var projects = solution.Projects
			.OrderBy(static project => project.Name, StringComparer.Ordinal)
			.Select(static project => new ProjectSummary(project.Name, project.FilePath))
			.ToArray();

		var baseline = await analysisService.AnalyzeScopeAsync(new AnalyzeScopeRequest(AnalysisScopes.Solution), cancellationToken).ConfigureAwait(false);
		var diagnostics = baseline.Diagnostics.ToDiagnosticsSummary();
		var readiness = AssessReadiness(solution, baseline.Diagnostics);

		var (workspaceVersion, versionError) = await solutionAccessor.GetWorkspaceVersionAsync(cancellationToken).ConfigureAwait(false);
		if (versionError is not null)
		{
			return new Result(
				select.SelectedSolutionPath,
				string.Empty,
				string.Empty,
				projects,
				diagnostics,
				readiness,
				versionError.ToLocalError("Retry load_solution to refresh workspace snapshot metadata."))
				.WithWorkspaceRelativePaths(_workspaceRoot);
		}

		var workspaceId = select.SelectedSolutionPath ?? string.Empty;
		var snapshotId = workspaceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
		return new Result(select.SelectedSolutionPath, workspaceId, snapshotId, projects, diagnostics, readiness)
			.WithWorkspaceRelativePaths(_workspaceRoot);
	}

	private Result Failure(WorkspaceReadiness readiness, ErrorInfo? error)
		=> new Result(
			null,
			string.Empty,
			string.Empty,
			Array.Empty<ProjectSummary>(),
			new DiagnosticsSummary(0, 0, 0, 0),
			readiness,
			error)
			.WithWorkspaceRelativePaths(_workspaceRoot);

	private static WorkspaceReadiness AssessReadiness(Solution solution, IReadOnlyList<DiagnosticItem> diagnostics)
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
			IsGeneratedLike(diagnostic.Location.FilePath)
			&& !string.Equals(diagnostic.Severity, "info", StringComparison.OrdinalIgnoreCase));

		if (generatedDiagnostics > 0)
		{
			return new WorkspaceReadiness(
				ReadinessStates.DegradedRestoreRecommended,
				["generated_or_intermediate_diagnostics"],
				"Navigation may still work, but running dotnet restore/build should improve workspace completeness.");
		}

		return DefaultReadiness;
	}

	private static async Task<(string? SolutionPath, RoslynMcp.Core.Models.ErrorInfo? Error)> ResolveSolutionPathAsync(
		string? hint,
		ISolutionSessionService solutionSessionService,
		CancellationToken cancellationToken)
	{
		if (IsExplicitSolutionPath(hint))
			return (hint, null);

		var root = string.IsNullOrWhiteSpace(hint) ? Directory.GetCurrentDirectory() : hint;
		var discovered = await solutionSessionService.DiscoverSolutionsAsync(new DiscoverSolutionsRequest(root), cancellationToken).ConfigureAwait(false);
		if (discovered.Error is not null)
			return (null, discovered.Error);

		if (discovered.SolutionPaths.Count == 0)
		{
			return (null, new RoslynMcp.Core.Models.ErrorInfo(
				ErrorCodes.SolutionNotFound,
				"No solution files were discovered.",
				new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["nextAction"] = "Provide a solution hint path or run load_solution from a workspace that contains a .sln or .slnx file."
				}));
		}

		return (discovered.SolutionPaths[0], null);
	}

	private static bool IsExplicitSolutionPath(string? hint)
		=> !string.IsNullOrWhiteSpace(hint)
		   && (hint.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
		       || hint.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));

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
}