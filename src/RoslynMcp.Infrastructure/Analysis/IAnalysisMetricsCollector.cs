using RoslynMcp.Core.Models.Analysis;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Analysis;

internal interface IAnalysisMetricsCollector
{
    Task<IReadOnlyList<MetricItem>> CollectMetricsAsync(Solution solution, string scope, string? path, CancellationToken ct);

    Task<IReadOnlyList<MetricItem>> CollectMetricsAsync(IEnumerable<Project> projects, string scope, string? path, CancellationToken ct);
}
