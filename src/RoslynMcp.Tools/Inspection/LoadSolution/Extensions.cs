using Microsoft.CodeAnalysis;
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
            return new DiagnosticsSummary(
                diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
                diagnostics.Count(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Info or DiagnosticSeverity.Hidden)
                );
        }
    }
}