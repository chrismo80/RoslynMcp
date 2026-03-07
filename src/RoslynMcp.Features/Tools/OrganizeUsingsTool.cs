using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class OrganizeUsingsTool(IRefactoringService refactoringService) : Tool
{
    private readonly IRefactoringService _refactoringService = refactoringService ?? throw new ArgumentNullException(nameof(refactoringService));

    [McpServerTool(Name = "organize_usings", Title = "Organize Usings")]
    [Description("Use this tool when you need to organize using directives in a C# file. This removes unused usings and sorts them alphabetically. Returns information about changes made.")]
    public Task<OrganizeUsingsResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("Path to the source file to organize. The file must exist in the currently loaded solution.")]
        string path,
        [Description("Whether to remove unused using directives. Default is true.")]
        bool? removeUnused = true,
        [Description("Whether to sort using directives alphabetically. Default is true.")]
        bool? sortUsings = true
        )
        => _refactoringService.OrganizeUsingsAsync(path.ToOrganizeUsingsRequest(removeUnused ?? true, sortUsings ?? true), cancellationToken);
}
