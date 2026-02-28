using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Analysis;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Infrastructure.Analysis;

public sealed class RoslynAnalysisService : IAnalysisService
{
    private readonly IRoslynSolutionAccessor _solutionAccessor;
    private readonly IAnalysisDiagnosticsRunner _diagnosticsRunner;
    private readonly IAnalysisMetricsCollector _metricsCollector;
    private readonly IAnalysisScopeResolver _scopeResolver;
    private readonly IAnalysisResultOrderer _resultOrderer;
    private readonly ILogger<RoslynAnalysisService> _logger;

    internal RoslynAnalysisService(
        IRoslynSolutionAccessor solutionAccessor,
        IAnalysisDiagnosticsRunner diagnosticsRunner,
        IAnalysisMetricsCollector metricsCollector,
        IAnalysisScopeResolver scopeResolver,
        IAnalysisResultOrderer resultOrderer,
        ILogger<RoslynAnalysisService>? logger = null)
    {
        _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));
        _diagnosticsRunner = diagnosticsRunner ?? throw new ArgumentNullException(nameof(diagnosticsRunner));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _scopeResolver = scopeResolver ?? throw new ArgumentNullException(nameof(scopeResolver));
        _resultOrderer = resultOrderer ?? throw new ArgumentNullException(nameof(resultOrderer));
        _logger = logger ?? NullLogger<RoslynAnalysisService>.Instance;
    }

    public RoslynAnalysisService(IRoslynSolutionAccessor solutionAccessor, ILogger<RoslynAnalysisService>? logger = null)
        : this(
            solutionAccessor,
            new AnalysisDiagnosticsRunner(new RoslynatorAnalyzerCatalog(), logger),
            new AnalysisMetricsCollector(new RoslynSymbolIdFactory()),
            new AnalysisScopeResolver(),
            new AnalysisResultOrderer(),
            logger)
    {
    }

    public async Task<AnalyzeSolutionResult> AnalyzeSolutionAsync(AnalyzeSolutionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        try
        {
            var (solution, error) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new AnalyzeSolutionResult(Array.Empty<DiagnosticItem>(),
                    error ?? new ErrorInfo(ErrorCodes.AnalysisFailed, "No solution has been selected."));
            }

            var diagnostics = await _diagnosticsRunner.RunDiagnosticsAsync(solution, ct).ConfigureAwait(false);
            return new AnalyzeSolutionResult(_resultOrderer.OrderDiagnostics(diagnostics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnalyzeSolution failed");
            return new AnalyzeSolutionResult(Array.Empty<DiagnosticItem>(),
                new ErrorInfo(ErrorCodes.AnalysisFailed, $"Diagnostics failed: {ex.Message}"));
        }
    }

    public async Task<GetCodeMetricsResult> GetCodeMetricsAsync(GetCodeMetricsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        try
        {
            var (solution, error) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetCodeMetricsResult(Array.Empty<MetricItem>(),
                    error ?? new ErrorInfo(ErrorCodes.AnalysisFailed, "No solution has been selected."));
            }

            var metrics = await _metricsCollector.CollectMetricsAsync(solution, AnalysisScopes.Solution, path: null, ct).ConfigureAwait(false);
            return new GetCodeMetricsResult(_resultOrderer.OrderMetrics(metrics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCodeMetrics failed");
            return new GetCodeMetricsResult(Array.Empty<MetricItem>(),
                new ErrorInfo(ErrorCodes.AnalysisFailed, $"Metrics collection failed: {ex.Message}"));
        }
    }

    public async Task<AnalyzeScopeResult> AnalyzeScopeAsync(AnalyzeScopeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (!_scopeResolver.IsValidScope(request.Scope))
        {
            return new AnalyzeScopeResult(
                request.Scope,
                request.Path,
                Array.Empty<DiagnosticItem>(),
                Array.Empty<MetricItem>(),
                new ErrorInfo(ErrorCodes.InvalidRequest, "scope must be one of: document, project, solution."));
        }

        if (string.Equals(request.Scope, AnalysisScopes.Document, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(request.Path))
        {
            return new AnalyzeScopeResult(
                request.Scope,
                request.Path,
                Array.Empty<DiagnosticItem>(),
                Array.Empty<MetricItem>(),
                new ErrorInfo(ErrorCodes.InvalidRequest, "path is required when scope is document."));
        }

        try
        {
            var (solution, error) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new AnalyzeScopeResult(request.Scope,
                    request.Path,
                    Array.Empty<DiagnosticItem>(),
                    Array.Empty<MetricItem>(),
                    error ?? new ErrorInfo(ErrorCodes.SolutionNotSelected, "No solution has been selected."));
            }

            var projects = _scopeResolver.ResolveProjectsForScope(solution, request.Scope, request.Path).ToArray();
            if (projects.Length == 0)
            {
                return new AnalyzeScopeResult(
                    request.Scope,
                    request.Path,
                    Array.Empty<DiagnosticItem>(),
                    Array.Empty<MetricItem>(),
                    new ErrorInfo(ErrorCodes.PathOutOfScope, "The provided path is outside the selected solution scope."));
            }

            var diagnostics = await _diagnosticsRunner.RunDiagnosticsAsync(projects, ct).ConfigureAwait(false);
            var metrics = await _metricsCollector.CollectMetricsAsync(projects, request.Scope, request.Path, ct).ConfigureAwait(false);
            return new AnalyzeScopeResult(
                request.Scope,
                request.Path,
                _resultOrderer.OrderDiagnostics(_scopeResolver.FilterDiagnosticsByScope(diagnostics, request.Scope, request.Path)),
                _resultOrderer.OrderMetrics(metrics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnalyzeScope failed for {Scope}:{Path}", request.Scope, request.Path);
            return new AnalyzeScopeResult(
                request.Scope,
                request.Path,
                Array.Empty<DiagnosticItem>(),
                Array.Empty<MetricItem>(),
                new ErrorInfo(ErrorCodes.AnalysisFailed, $"Scoped analysis failed: {ex.Message}"));
        }
    }
}
