using RoslynMcp.Core.Models.Analysis;

namespace RoslynMcp.Infrastructure.Analysis;

internal interface IAnalysisResultOrderer
{
    IReadOnlyList<DiagnosticItem> OrderDiagnostics(IReadOnlyList<DiagnosticItem> diagnostics);

    IReadOnlyList<MetricItem> OrderMetrics(IReadOnlyList<MetricItem> metrics);
}
