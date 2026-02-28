using RoslynMcp.Core.Models.Analysis;

namespace RoslynMcp.Core.Contracts;

public interface IAnalysisService
{
    Task<AnalyzeSolutionResult> AnalyzeSolutionAsync(AnalyzeSolutionRequest request, CancellationToken ct);
    Task<AnalyzeScopeResult> AnalyzeScopeAsync(AnalyzeScopeRequest request, CancellationToken ct);
    Task<GetCodeMetricsResult> GetCodeMetricsAsync(GetCodeMetricsRequest request, CancellationToken ct);
}
