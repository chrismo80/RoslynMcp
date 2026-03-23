using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Infrastructure;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadSolution;

internal static class Extensions
{
    extension(Project project)
    {
        public ProjectSummary ToSummary(WorkspaceManager workspaceManager) =>
            new(project.Name, workspaceManager.ToRelativePathIfPossible(project.FilePath));

        public async IAsyncEnumerable<Diagnostic> Diagnose(CancellationToken cancellationToken)
        {
            var compilation = await project
                .GetCompilationAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
                yield return diagnostic;
        }
    }

    extension(IReadOnlyList<Diagnostic> diagnostics)
    {
        public DiagnosticsSummary ToDiagnosticsSummary()
        {
            var filtered = diagnostics
                .Where(static diagnostic => SourceVisibility.ShouldIncludeInHumanResults(diagnostic.Location.GetLineSpan().Path))
                .ToArray();

            return new DiagnosticsSummary(
                filtered.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                filtered.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
                filtered.Count(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Info or DiagnosticSeverity.Hidden),
                filtered.Length);
        }
    }
}