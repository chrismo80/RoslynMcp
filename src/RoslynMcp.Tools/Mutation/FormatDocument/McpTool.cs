using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Mutation.FormatDocument;

public sealed record Result(string Path, ErrorInfo? Error = null);

public sealed record ErrorInfo(
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);

[McpServerToolType]
public sealed class McpTool(
    SolutionManager solutionManager,
    WorkspaceManager workspaceManager)
    : Tool
{
    [McpServerTool(Name = "format_document", Title = "Format Document")]
    [Description(
        "Use this tool when you need to format exactly one C# source document in the loaded solution using the solution's current formatting and style settings. Returns whether formatting changes were applied and persisted.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("The path to the C# source file to format. The file must be part of the currently loaded solution.")]
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Result(path, new ErrorInfo("path must be provided.",
                new Dictionary<string, string>
                {
                    ["field"] = "path"
                }));
        }
        
        var document = FindDocument(workspaceManager.ToAbsolutePath(path));

        var refreshed = await RefreshFromDiskAsync(document, cancellationToken).ConfigureAwait(false);
        
        if (refreshed.Error is not null)
            return new Result(path, refreshed.Error);
        
        var formatted = await Formatter.FormatAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        var updatedSolution = formatted.Project.Solution;
        
        if (!solutionManager.TryApplyChanges(updatedSolution))
            return new Result(path, new ErrorInfo("Failed to apply formatted document changes."));

        return new Result(path);
    }
    
     private Microsoft.CodeAnalysis.Document? FindDocument(string requestedPath)
    {
        return solutionManager.Solution?.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => document.FilePath == requestedPath);
    }

    private static async Task<(Document? Document, ErrorInfo? Error)> RefreshFromDiskAsync(Microsoft.CodeAnalysis.Document document, CancellationToken cancellationToken)
    {
        if (!File.Exists(document.FilePath))
            return (null, new ErrorInfo("Workspace snapshot is stale relative to filesystem. Reload the solution, then retry."));

        try
        {
            var documentText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            
            await using var stream = new FileStream(document.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            
            var fileText = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            
            var sourceText = SourceText.From(fileText);
            
            return documentText.ContentEquals(sourceText)
                ? (document, null)
                : (document.WithText(sourceText), null);
        }
        catch (IOException)
        {
            return (null, new ErrorInfo("Workspace snapshot is stale relative to filesystem. Reload the solution, then retry."));
        }
        catch (UnauthorizedAccessException)
        {
            return (null, new ErrorInfo("Workspace snapshot is stale relative to filesystem. Reload the solution, then retry."));
        }
    }
}
