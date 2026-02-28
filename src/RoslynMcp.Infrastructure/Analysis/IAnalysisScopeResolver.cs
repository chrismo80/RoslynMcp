using RoslynMcp.Core.Models.Analysis;

namespace RoslynMcp.Infrastructure.Analysis;

internal interface IAnalysisScopeResolver
{
    bool IsValidScope(string scope);

    IEnumerable<Microsoft.CodeAnalysis.Project> ResolveProjectsForScope(Microsoft.CodeAnalysis.Solution solution, string scope, string? path);

    IReadOnlyList<DiagnosticItem> FilterDiagnosticsByScope(IReadOnlyList<DiagnosticItem> diagnostics, string scope, string? path);

    bool IsDocumentInScope(Microsoft.CodeAnalysis.Document document, string scope, string? path);
}
