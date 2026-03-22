using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Mutation;

namespace RoslynMcp.Tools.Mutation.FormatDocument;

public sealed class Service(Workspace workspace)
{
    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return new Result(request.Path, false, new ErrorInfo("invalid_input", "path must be provided.", new Dictionary<string, string> { ["field"] = "path" })).WithWorkspaceRelativePaths(Path.GetFullPath(Directory.GetCurrentDirectory()));

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
            return new Result(request.Path, false, new ErrorInfo("no_solution_loaded", "No solution is currently loaded.")).WithWorkspaceRelativePaths(Path.GetFullPath(Directory.GetCurrentDirectory()));

        var solutionDirectory = Path.GetDirectoryName(session.SelectedSolutionPath) ?? session.WorkspaceRoot;
        var outwardPath = GetOutwardPath(request.Path, session.WorkspaceRoot, solutionDirectory);
        var document = FindDocument(session.Solution, request.Path, session.WorkspaceRoot, solutionDirectory);
        if (document is null)
            return new Result(outwardPath, false, new ErrorInfo("path_out_of_scope", "The provided path does not match a document in the selected solution scope.", new Dictionary<string, string> { ["path"] = outwardPath }));

        var refreshedDocument = await RefreshFromDiskAsync(document, cancellationToken).ConfigureAwait(false);
        if (refreshedDocument.Error is not null)
            return new Result(outwardPath, false, refreshedDocument.Error);

        var workingDocument = refreshedDocument.Document ?? document;
        var successPath = Path.IsPathRooted(request.Path) ? GetOutwardPath(workingDocument.FilePath ?? workingDocument.Name, session.WorkspaceRoot, solutionDirectory) : outwardPath;
        var updatedSolution = await workingDocument.FormatDocumentAsync(cancellationToken).ConfigureAwait(false);
        var changedFiles = await session.Solution.CollectChangedFilesAsync(updatedSolution, cancellationToken).ConfigureAwait(false);
        if (changedFiles.Count == 0)
            return new Result(successPath, false);

        if (!session.Workspace.TryApplyChanges(updatedSolution))
            return new Result(successPath, false, new ErrorInfo("internal_error", "Failed to apply formatted document changes."));

        return new Result(successPath, true);
    }

    private static string GetOutwardPath(string path, string workspaceRoot, string solutionDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (!Path.IsPathRooted(path))
            return path.Trim();

        var solutionRelative = path.ToWorkspaceRelativePathIfPossible(solutionDirectory);
        if (!Path.IsPathRooted(solutionRelative))
            return solutionRelative;

        return path.ToWorkspaceRelativePathIfPossible(workspaceRoot);
    }

    private static Microsoft.CodeAnalysis.Document? FindDocument(Microsoft.CodeAnalysis.Solution solution, string requestedPath, string workspaceRoot, string solutionDirectory)
    {
        var documents = solution.Projects.SelectMany(static project => project.Documents).ToArray();
        var resolvedPath = requestedPath.ResolvePathForMutationLookup(workspaceRoot, solutionDirectory);
        var direct = documents.FirstOrDefault(candidate => candidate.FilePath.MatchesByNormalizedPath(resolvedPath));
        if (direct is not null)
            return direct;

        if (Path.IsPathRooted(requestedPath))
            return null;

        var normalizedRelativePath = requestedPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        return documents.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.FilePath)
            && candidate.FilePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).EndsWith(normalizedRelativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(Microsoft.CodeAnalysis.Document? Document, ErrorInfo? Error)> RefreshFromDiskAsync(Microsoft.CodeAnalysis.Document document, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.FilePath) || !Path.IsPathRooted(document.FilePath))
            return (document, null);

        if (!File.Exists(document.FilePath))
            return (null, new ErrorInfo("stale_workspace_snapshot", "Workspace snapshot is stale relative to filesystem. Reload the solution, then retry."));

        try
        {
            var documentText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = new FileStream(document.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var fileText = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var sourceText = Microsoft.CodeAnalysis.Text.SourceText.From(fileText);
            return documentText.ContentEquals(sourceText)
                ? (document, null)
                : (document.WithText(sourceText), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return (null, new ErrorInfo("stale_workspace_snapshot", "Workspace snapshot is stale relative to filesystem. Reload the solution, then retry."));
        }
        catch (UnauthorizedAccessException)
        {
            return (null, new ErrorInfo("stale_workspace_snapshot", "Workspace snapshot is stale relative to filesystem. Reload the solution, then retry."));
        }
    }
}
