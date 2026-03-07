using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class FindCodeSmellsTool(ICodeSmellFindingService codeSmellFindingService) : Tool
{
    private readonly ICodeSmellFindingService _codeSmellFindingService = codeSmellFindingService ?? throw new ArgumentNullException(nameof(codeSmellFindingService));

    [McpServerTool(Name = "find_codesmells", Title = "Find Code Smells", ReadOnly = true, Idempotent = true)]
        [Description("Use this tool when you need to check a specific file for potential code quality issues. It runs Roslyn-based static analysis to detect common problems such as dead code, performance anti-patterns, naming violations, and other code smells identified by Roslynator analyzers. Optional filters let you limit findings by normalized risk level, category, and result count in deterministic stream order.")]
    public Task<FindCodeSmellsResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("Path to the source file to analyze. The file must exist in the currently loaded solution.")]
        string path,
        [Description("Maximum number of accepted findings to return. When provided, discovery stops as soon as this many matching findings are found.")]
        int? maxFindings = null,
        [Description("Accepted risk levels to include. Use normalized result values such as low, review_required, high, or info.")]
        IReadOnlyList<string>? riskLevels = null,
        [Description("Accepted categories to include. When omitted or empty, all categories are included.")]
        IReadOnlyList<string>? categories = null
        )
        => _codeSmellFindingService.FindCodeSmellsAsync(path.ToFindCodeSmellsRequest(maxFindings, riskLevels, categories), cancellationToken);
}
