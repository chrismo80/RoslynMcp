using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.FindCodeSmells;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "find_codesmells", Title = "Find Code Smells", ReadOnly = true, Idempotent = true)]
	[Description("Use this tool when you need to check a specific file for potential code quality issues. It runs Roslyn-based static analysis to detect common problems such as dead code, performance anti-patterns, naming violations, and other code smells identified by Roslynator analyzers. Optional filters operate on stable normalized risk levels (low, review_required, high, info) and categories (analyzer, correctness, design, maintainability, performance, style) in deterministic stream order. reviewMode=conservative favors stronger review signals over low-noise style and trivia suggestions.")]
	public Task<Result> Run(CancellationToken cancellationToken,
		[Description("Path to the source file to analyze. The file must exist in the currently loaded solution.")]
		string path,
		[Description("Maximum number of accepted findings to return. When provided, discovery stops as soon as this many matching findings are found.")]
		int? maxFindings = null,
		[Description("Accepted risk levels to include. Use normalized result values such as low, review_required, high, or info.")]
		IReadOnlyList<string>? riskLevels = null,
		[Description("Accepted categories to include. Use normalized values: analyzer, correctness, design, maintainability, performance, or style. When omitted or empty, all categories are included.")]
		IReadOnlyList<string>? categories = null,
		[Description("Review ranking mode. Use 'default' for the existing stream or 'conservative' to suppress lightweight style/trivia noise when stronger issues are present.")]
		string? reviewMode = null)
		=> service.RunAsync(path.ToRequest(maxFindings, riskLevels, categories, reviewMode), cancellationToken);
}
