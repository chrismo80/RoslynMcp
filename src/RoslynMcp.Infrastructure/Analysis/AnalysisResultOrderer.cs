using RoslynMcp.Core.Models.Analysis;

namespace RoslynMcp.Infrastructure.Analysis;

internal sealed class AnalysisResultOrderer : IAnalysisResultOrderer
{
    public IReadOnlyList<DiagnosticItem> OrderDiagnostics(IReadOnlyList<DiagnosticItem> diagnostics)
        => diagnostics
            .OrderBy(static item => item.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(static item => item.Location.Line)
            .ThenBy(static item => item.Location.Column)
            .ThenBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Message, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyList<MetricItem> OrderMetrics(IReadOnlyList<MetricItem> metrics)
        => metrics
            .OrderBy(static item => item.SymbolId, StringComparer.Ordinal)
            .ThenBy(static item => item.CyclomaticComplexity ?? -1)
            .ThenBy(static item => item.LineCount ?? -1)
            .ToList();
}
