using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Analysis;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Infrastructure.Workspace;

namespace RoslynMcp.Infrastructure.Agent;

public sealed class WorkspaceBootstrapService : IWorkspaceBootstrapService
{
    private readonly ISolutionSessionService _solutionSessionService;
    private readonly IAnalysisService _analysisService;
    private readonly IRoslynSolutionAccessor _solutionAccessor;

    public WorkspaceBootstrapService(
        ISolutionSessionService solutionSessionService,
        IAnalysisService analysisService,
        IRoslynSolutionAccessor solutionAccessor)
    {
        _solutionSessionService = solutionSessionService;
        _analysisService = analysisService;
        _solutionAccessor = solutionAccessor;
    }

    public async Task<LoadSolutionResult> LoadSolutionAsync(LoadSolutionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var hint = request.SolutionHintPath?.Trim();
        var (solutionPath, discoveryError) = await ResolveSolutionPathAsync(hint, ct).ConfigureAwait(false);
        if (solutionPath == null)
        {
            return new LoadSolutionResult(
                null,
                string.Empty,
                string.Empty,
                Array.Empty<ProjectSummary>(),
                new DiagnosticsSummary(0, 0, 0, 0),
                AgentErrorInfo.Normalize(discoveryError, "Provide a valid solution path or run load_solution from a folder containing a .sln file."));
        }

        var select = await _solutionSessionService.SelectSolutionAsync(new SelectSolutionRequest(solutionPath), ct).ConfigureAwait(false);
        if (select.Error != null)
        {
            return new LoadSolutionResult(
                null,
                string.Empty,
                string.Empty,
                Array.Empty<ProjectSummary>(),
                new DiagnosticsSummary(0, 0, 0, 0),
                AgentErrorInfo.Normalize(select.Error, "Provide a valid .sln path and retry load_solution."));
        }

        var (solution, currentError) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new LoadSolutionResult(
                select.SelectedSolutionPath,
                string.Empty,
                string.Empty,
                Array.Empty<ProjectSummary>(),
                new DiagnosticsSummary(0, 0, 0, 0),
                AgentErrorInfo.Normalize(currentError, "Retry load_solution after the workspace/session is available."));
        }

        var projects = solution.Projects
            .OrderBy(static p => p.Name, StringComparer.Ordinal)
            .Select(static p => new ProjectSummary(p.Name, p.FilePath))
            .ToArray();

        var baseline = await _analysisService.AnalyzeScopeAsync(new AnalyzeScopeRequest(AnalysisScopes.Solution), ct).ConfigureAwait(false);
        var diagnostics = ToSummary(baseline.Diagnostics);

        var (workspaceVersion, versionError) = await _solutionAccessor.GetWorkspaceVersionAsync(ct).ConfigureAwait(false);
        if (versionError != null)
        {
            return new LoadSolutionResult(select.SelectedSolutionPath,
                string.Empty,
                string.Empty,
                projects,
                diagnostics,
                AgentErrorInfo.Normalize(versionError, "Retry load_solution to refresh workspace snapshot metadata."));
        }

        var workspaceId = select.SelectedSolutionPath ?? string.Empty;
        var snapshotId = workspaceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new LoadSolutionResult(select.SelectedSolutionPath, workspaceId, snapshotId, projects, diagnostics);
    }

    private async Task<(string? Path, ErrorInfo? Error)> ResolveSolutionPathAsync(string? hint, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(hint) && hint.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return (hint, null);
        }

        var root = string.IsNullOrWhiteSpace(hint) ? Directory.GetCurrentDirectory() : hint;
        var discovered = await _solutionSessionService.DiscoverSolutionsAsync(new DiscoverSolutionsRequest(root), ct).ConfigureAwait(false);
        if (discovered.Error != null)
        {
            return (null, discovered.Error);
        }

        if (discovered.SolutionPaths.Count == 0)
        {
            return (null, AgentErrorInfo.Create(
                "solution_not_found",
                "No solution files were discovered.",
                "Provide a solution hint path or run load_solution from a workspace that contains a .sln file."));
        }

        return (discovered.SolutionPaths[0], null);
    }

    private static DiagnosticsSummary ToSummary(IReadOnlyList<DiagnosticItem> diagnostics)
    {
        var error = diagnostics.Count(static d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var warning = diagnostics.Count(static d => string.Equals(d.Severity, "warning", StringComparison.OrdinalIgnoreCase));
        var info = diagnostics.Count - error - warning;
        return new DiagnosticsSummary(error, warning, info, diagnostics.Count);
    }
}
