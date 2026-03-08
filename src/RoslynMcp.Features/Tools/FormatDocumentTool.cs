using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class FormatDocumentTool(IRefactoringService refactoringService) : Tool
{
    private readonly IRefactoringService _refactoringService = refactoringService ?? throw new ArgumentNullException(nameof(refactoringService));

    [McpServerTool(Name = "format_document", Title = "Format Document")]
    [Description("Use this tool when you need to format a C# source file. This applies standard C# code formatting (indentation, spacing, braces) without changing code semantics. Returns information about formatting changes.")]
    public Task<FormatDocumentResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("Path to the source file to format. The file must exist in the currently loaded solution.")]
        string path
        )
        => _refactoringService.FormatDocumentAsync(path.ToFormatDocumentRequest(), cancellationToken);
}
