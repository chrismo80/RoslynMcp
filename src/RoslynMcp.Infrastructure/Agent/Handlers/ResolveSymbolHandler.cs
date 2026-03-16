using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Navigation;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

/// <summary>
/// Resolves symbols by symbolId, source position, or qualified name.
/// Returns symbol metadata including kind, accessibility, and location.
/// </summary>
internal sealed class ResolveSymbolHandler(CodeUnderstandingQueryService queries, ISymbolLookupService symbolLookupService)
{
    public async Task<ResolveSymbolResult> HandleAsync(ResolveSymbolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before resolving symbols.",
            request.Path ?? request.ProjectPath,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ResolveSymbolResult(null, false, [],
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before resolving symbols."));
        }

        if (!string.IsNullOrWhiteSpace(request.SymbolId))
        {
            var symbol = await symbolLookupService.ResolveSymbolAsync(request.SymbolId!, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new ResolveSymbolResult(null, false, [],
                    AgentErrorInfo.Create(
                        ErrorCodes.SymbolNotFound,
                        $"symbolId '{request.SymbolId}' could not be resolved.",
                        "Call list_types/list_members or explain_symbol first to obtain a valid symbolId.",
                        ("field", "symbolId"),
                        ("provided", request.SymbolId)));
            }

            return new ResolveSymbolResult(symbol.ToResolvedSymbol(), false, Array.Empty<ResolveSymbolCandidate>());
        }

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
        {
            var symbol = await symbolLookupService.GetSymbolAtPositionAsync(
                solution,
                request.Path!,
                request.Line.Value,
                request.Column.Value,
                ct).ConfigureAwait(false);

            if (symbol == null)
            {
                return new ResolveSymbolResult(null, false, [],
                    AgentErrorInfo.Create(
                        ErrorCodes.SymbolNotFound,
                        "No symbol found at the provided source position.",
                        "Call resolve_symbol with a valid path+line+column or use list_types/list_members to select a symbolId.",
                        ("field", "path"),
                        ("provided", request.Path)));
            }

            return new ResolveSymbolResult(symbol.ToResolvedSymbol(), false, Array.Empty<ResolveSymbolCandidate>());
        }

        if (string.IsNullOrWhiteSpace(request.QualifiedName))
        {
            return new ResolveSymbolResult(null, false, [],
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "Provide symbolId, path+line+column, or qualifiedName.",
                    "Call resolve_symbol with one selector mode: symbolId, source position, or qualifiedName."));
        }

        var selectedProjects = solution.ResolveProjectSelector(
            request.ProjectPath,
            request.ProjectName,
            request.ProjectId,
            selectorRequired: false,
            toolName: "resolve_symbol",
            out var selectorError);

        if (selectorError != null)
            return new ResolveSymbolResult(null, false, Array.Empty<ResolveSymbolCandidate>(), selectorError);

        var candidates = await request.QualifiedName!.ResolveByQualifiedNameAsync(selectedProjects, ct).ConfigureAwait(false);
        if (candidates.Length == 0)
        {
            return new ResolveSymbolResult(
                null,
                false,
                Array.Empty<ResolveSymbolCandidate>(),
                AgentErrorInfo.Create(
                    ErrorCodes.SymbolNotFound,
                    $"qualifiedName '{request.QualifiedName}' did not match any symbol.",
                    "Refine qualifiedName or provide projectName/projectPath/projectId to narrow the lookup.",
                    ("field", "qualifiedName"),
                    ("provided", request.QualifiedName)));
        }

        if (candidates.Length > 1)
        {
            candidates = QualifyCandidatesForDisambiguation(candidates);
            return new ResolveSymbolResult(
                null,
                true,
                candidates,
                AgentErrorInfo.Create(
                    ErrorCodes.AmbiguousSymbol,
                    $"qualifiedName '{request.QualifiedName}' matched multiple symbols.",
                    "Select one candidate symbolId and call resolve_symbol again, or scope by projectName/projectPath/projectId.",
                    ("field", "qualifiedName"),
                    ("provided", request.QualifiedName),
                    ("candidateCount", candidates.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        var selected = candidates[0];
        return new ResolveSymbolResult(
            new ResolvedSymbolSummary(selected.SymbolId, selected.DisplayName, selected.Kind, selected.Location),
            false, []);

    }

    private static ResolveSymbolCandidate[] QualifyCandidatesForDisambiguation(IReadOnlyList<ResolveSymbolCandidate> candidates)
        => candidates
            .Select(candidate => candidate with { QualifiedDisplayName = candidate.QualifiedDisplayName ?? candidate.DisplayName })
            .ToArray();
}
