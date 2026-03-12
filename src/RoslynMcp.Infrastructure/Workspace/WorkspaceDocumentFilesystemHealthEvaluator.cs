using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Infrastructure.Workspace;

internal sealed record WorkspaceDocumentFilesystemHealth(
    bool IsConsistent,
    IReadOnlyList<string> MissingRootedFiles,
    IReadOnlyList<string> DriftedRootedFiles);

internal static class WorkspaceDocumentFilesystemHealthEvaluator
{
    public static async Task<WorkspaceDocumentFilesystemHealth> EvaluateAsync(
        IEnumerable<Document> documents,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var missingRootedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var driftedRootedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            ct.ThrowIfCancellationRequested();

            var filePath = document.FilePath;
            if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
            {
                continue;
            }

            if (!File.Exists(filePath))
            {
                missingRootedFiles.Add(filePath);
                continue;
            }

            var documentText = await document.GetTextAsync(ct).ConfigureAwait(false);
            SourceText? fileText;
            try
            {
                fileText = await ReadFileTextAsync(filePath, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException)
            {
                missingRootedFiles.Add(filePath);
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                missingRootedFiles.Add(filePath);
                continue;
            }

            if (!documentText.ContentEquals(fileText))
            {
                driftedRootedFiles.Add(filePath);
            }
        }

        var missingFiles = missingRootedFiles
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var driftedFiles = driftedRootedFiles
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WorkspaceDocumentFilesystemHealth(
            missingFiles.Length == 0 && driftedFiles.Length == 0,
            missingFiles,
            driftedFiles);
    }

    private static async Task<SourceText> ReadFileTextAsync(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        return SourceText.From(text);
    }
}
