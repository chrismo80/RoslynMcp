using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.FormatDocument;

public sealed class Service(Workspace workspace)
{
	public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Path))
			return new Result(request.Path, false, new ErrorInfo("invalid_input", "path must be provided.", new Dictionary<string, string> { ["field"] = "path" })).WithWorkspaceRelativePaths();

		var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
		if (session is null)
			return new Result(request.Path, false, new ErrorInfo("no_solution_loaded", "No solution is currently loaded.")).WithWorkspaceRelativePaths();

		var document = session.Solution.Projects.SelectMany(static project => project.Documents).FirstOrDefault(candidate => candidate.FilePath.MatchesByNormalizedPath(request.Path));
		if (document is null)
			return new Result(request.Path, false, new ErrorInfo("path_out_of_scope", "The provided path does not match a document in the selected solution scope.", new Dictionary<string, string> { ["path"] = request.Path })).WithWorkspaceRelativePaths();

		var updatedSolution = await document.FormatDocumentAsync(cancellationToken).ConfigureAwait(false);
		var changedFiles = await session.Solution.CollectChangedFilesAsync(updatedSolution, cancellationToken).ConfigureAwait(false);
		if (changedFiles.Count == 0)
			return new Result(document.FilePath ?? document.Name, false).WithWorkspaceRelativePaths();

		if (!session.Workspace.TryApplyChanges(updatedSolution))
			return new Result(document.FilePath ?? document.Name, false, new ErrorInfo("internal_error", "Failed to apply formatted document changes.")).WithWorkspaceRelativePaths();

		return new Result(document.FilePath ?? document.Name, true).WithWorkspaceRelativePaths();
	}
}
