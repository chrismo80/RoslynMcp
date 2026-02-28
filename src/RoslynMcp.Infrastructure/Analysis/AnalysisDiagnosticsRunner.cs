using RoslynMcp.Core.Models.Analysis;
using RoslynMcp.Core.Models.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;

namespace RoslynMcp.Infrastructure.Analysis;

internal sealed class AnalysisDiagnosticsRunner : IAnalysisDiagnosticsRunner
{
    private static bool s_analyzerLoadLogged;

    private readonly IRoslynAnalyzerCatalog _analyzerCatalog;
    private readonly ILogger _logger;

    public AnalysisDiagnosticsRunner(IRoslynAnalyzerCatalog analyzerCatalog, ILogger? logger = null)
    {
        _analyzerCatalog = analyzerCatalog ?? throw new ArgumentNullException(nameof(analyzerCatalog));
        _logger = logger ?? NullLogger.Instance;
    }

    public Task<IReadOnlyList<DiagnosticItem>> RunDiagnosticsAsync(Solution solution, CancellationToken ct)
        => RunDiagnosticsAsync(solution.Projects, ct);

    public async Task<IReadOnlyList<DiagnosticItem>> RunDiagnosticsAsync(IEnumerable<Project> projects, CancellationToken ct)
    {
        var diagnostics = new List<DiagnosticItem>();
        var analyzerEntry = _analyzerCatalog.GetCatalog();
        if (analyzerEntry.Error != null && !s_analyzerLoadLogged)
        {
            _logger.LogWarning(analyzerEntry.Error, "Failed to load Roslynator analyzers.");
            s_analyzerLoadLogged = true;
        }

        foreach (var project in projects.OrderBy(static p => p.FilePath ?? p.Name, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                continue;
            }

            ImmutableArray<Diagnostic> projectDiagnostics;
            if (!analyzerEntry.Analyzers.IsDefaultOrEmpty)
            {
                var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty);
                var options = new CompilationWithAnalyzersOptions(analyzerOptions,
                    (ex, analyzer, _) => _logger.LogWarning(ex, "Analyzer {Analyzer} failed", analyzer?.GetType().Name),
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false);

                var compilationWithAnalyzers = compilation.WithAnalyzers(analyzerEntry.Analyzers, options);
                projectDiagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync(ct).ConfigureAwait(false);
            }
            else
            {
                projectDiagnostics = compilation.GetDiagnostics(ct);
            }

            foreach (var diagnostic in projectDiagnostics)
            {
                diagnostics.Add(CreateDiagnosticItem(diagnostic));
            }
        }

        return diagnostics;
    }

    private static DiagnosticItem CreateDiagnosticItem(Diagnostic diagnostic)
    {
        var location = diagnostic.Location;
        var sourceLocation = location.IsInSource ? CreateSourceLocation(location) : new SourceLocation(string.Empty, 1, 1);
        return new DiagnosticItem(diagnostic.Id, NormalizeSeverity(diagnostic.Severity), diagnostic.GetMessage(), sourceLocation);
    }

    private static SourceLocation CreateSourceLocation(Location location)
    {
        var span = location.GetLineSpan();
        var filePath = span.Path ?? string.Empty;
        var start = span.StartLinePosition;
        return new SourceLocation(filePath, start.Line + 1, start.Character + 1);
    }

    private static string NormalizeSeverity(DiagnosticSeverity severity)
        => severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info"
        };
}
