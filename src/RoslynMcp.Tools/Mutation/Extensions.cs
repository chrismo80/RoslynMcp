using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Mutation;

internal static class Extensions
{
    internal static string ToWorkspaceRelativePathForMutation(this string path, string workspaceRoot)
        => path.ToWorkspaceRelativePathIfPossible(workspaceRoot);

    internal static async Task<IReadOnlyList<string>> CollectChangedFilesAsync(this Solution beforeSolution, Solution afterSolution, CancellationToken cancellationToken)
    {
        var changes = afterSolution.GetChanges(beforeSolution);
        var ids = changes.GetProjectChanges().SelectMany(static project => project.GetChangedDocuments()).Distinct().ToArray();
        var files = ids
            .Select(id => afterSolution.GetDocument(id)?.FilePath ?? afterSolution.GetDocument(id)?.Name)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(static path => path.ToWorkspaceRelativePathIfPossible())
            .ToArray();
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        return files;
    }

    internal static string ResolvePathForMutationLookup(this string requestedPath, string workspaceRoot, string solutionDirectory)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            return requestedPath;

        var trimmedPath = requestedPath.Trim();
        if (Path.IsPathRooted(trimmedPath))
            return Path.GetFullPath(trimmedPath);

        var workspaceCandidate = Path.GetFullPath(trimmedPath, workspaceRoot);
        if (File.Exists(workspaceCandidate))
            return workspaceCandidate;

        var solutionCandidate = Path.GetFullPath(trimmedPath, solutionDirectory);
        return File.Exists(solutionCandidate) ? solutionCandidate : workspaceCandidate;
    }

    internal static async Task<Solution> FormatDocumentAsync(this Document document, CancellationToken cancellationToken)
    {
        var formatted = await Microsoft.CodeAnalysis.Formatting.Formatter.FormatAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        return formatted.Project.Solution;
    }

    internal static async Task<(Document? Document, ErrorInfo? Error)> RefreshFromDiskAsync(this Document document, CancellationToken cancellationToken)
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
                : (null, new ErrorInfo("stale_workspace_snapshot", "Workspace snapshot is stale relative to filesystem. Reload the solution, then retry."));
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

    internal static string? NormalizeSymbolId(this string? symbolId)
        => symbolId.NormalizeOptional();

    internal static string DecodeHtmlEntities(this string value)
        => System.Net.WebUtility.HtmlDecode(value);
}
