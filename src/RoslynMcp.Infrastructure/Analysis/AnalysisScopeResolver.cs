using RoslynMcp.Core.Models.Analysis;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Analysis;

internal sealed class AnalysisScopeResolver : IAnalysisScopeResolver
{
    public bool IsValidScope(string scope)
        => string.Equals(scope, AnalysisScopes.Document, StringComparison.Ordinal)
           || string.Equals(scope, AnalysisScopes.Project, StringComparison.Ordinal)
           || string.Equals(scope, AnalysisScopes.Solution, StringComparison.Ordinal);

    public IEnumerable<Project> ResolveProjectsForScope(Solution solution, string scope, string? path)
    {
        if (string.Equals(scope, AnalysisScopes.Solution, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(path))
        {
            return solution.Projects;
        }

        if (string.Equals(scope, AnalysisScopes.Project, StringComparison.Ordinal))
        {
            return solution.Projects.Where(project =>
                MatchesByNormalizedPath(project.FilePath, path)
                || string.Equals(project.Name, path, StringComparison.OrdinalIgnoreCase));
        }

        return solution.Projects.Where(project =>
            project.Documents.Any(document => MatchesByNormalizedPath(document.FilePath, path)));
    }

    public IReadOnlyList<DiagnosticItem> FilterDiagnosticsByScope(IReadOnlyList<DiagnosticItem> diagnostics, string scope, string? path)
    {
        if (string.Equals(scope, AnalysisScopes.Solution, StringComparison.Ordinal)
            || string.Equals(scope, AnalysisScopes.Project, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(path))
        {
            return diagnostics;
        }

        if (string.Equals(scope, AnalysisScopes.Document, StringComparison.Ordinal))
        {
            return diagnostics.Where(diag => MatchesByNormalizedPath(diag.Location.FilePath, path)).ToList();
        }

        return diagnostics;
    }

    public bool IsDocumentInScope(Document document, string scope, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(scope, AnalysisScopes.Solution, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(scope, AnalysisScopes.Document, StringComparison.Ordinal))
        {
            return MatchesByNormalizedPath(document.FilePath, path);
        }

        var projectPath = document.Project.FilePath;
        return MatchesByNormalizedPath(projectPath, path)
               || string.Equals(document.Project.Name, path, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesByNormalizedPath(string? candidatePath, string path)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var normalizedCandidate = Path.GetFullPath(candidatePath);
            var normalizedPath = Path.GetFullPath(path);
            return string.Equals(normalizedCandidate, normalizedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(candidatePath, path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
