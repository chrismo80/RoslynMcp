using RoslynMcp.Core.Models.Analysis;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Analysis;

internal interface IAnalysisDiagnosticsRunner
{
    Task<IReadOnlyList<DiagnosticItem>> RunDiagnosticsAsync(Solution solution, CancellationToken ct);

    Task<IReadOnlyList<DiagnosticItem>> RunDiagnosticsAsync(IEnumerable<Project> projects, CancellationToken ct);
}
