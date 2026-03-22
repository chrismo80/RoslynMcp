using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Mutation;

internal static class Extensions
{
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

    internal static async Task<Solution> FormatDocumentAsync(this Document document, CancellationToken cancellationToken)
    {
        var formatted = await Microsoft.CodeAnalysis.Formatting.Formatter.FormatAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        return formatted.Project.Solution;
    }

    internal static string? NormalizeSymbolId(this string? symbolId)
        => symbolId.NormalizeOptional();
}
