using RoslynMcp.Tools.Infrastructure;
using RoslynMcp.Tools.Infrastructure.Services;

namespace RoslynMcp.Tools.Inspection.ResolveSymbol;

public sealed class Service(Infrastructure.Services.Workspace workspace)
{
    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return new Result(null, false, [], new ErrorInfo(
                "no_solution_loaded",
                "No solution is currently loaded.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nextAction"] = "Call load_solution first to select a solution before resolving symbols."
                }));
        }

        var currentWorkspaceRoot = Path.GetDirectoryName(session.SelectedSolutionPath)
            ?? Path.GetFullPath(Directory.GetCurrentDirectory());

        if (!string.IsNullOrWhiteSpace(request.SymbolId))
            return await ResolveBySymbolIdAsync(request, session.Solution, currentWorkspaceRoot, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
            return await ResolveByPositionAsync(request, session.Solution, currentWorkspaceRoot, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.QualifiedName))
        {
            return new Result(null, false, [], new ErrorInfo(
                "invalid_input",
                "Provide symbolId, path+line+column, or qualifiedName.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nextAction"] = "Call resolve_symbol with one selector mode: symbolId, source position, or qualifiedName."
                }));
        }

        var selectedProjects = session.Solution.Resolve(request.ProjectPath, request.ProjectName, request.ProjectId, selectorRequired: false, toolName: "resolve_symbol", out var selectorError);
        if (selectorError is not null)
            return new Result(null, false, [], new ErrorInfo(selectorError.Code, selectorError.Message, selectorError.Details)).WithWorkspaceRelativePaths(currentWorkspaceRoot);

        var candidates = await request.QualifiedName.ResolveByQualifiedNameAsync(selectedProjects, cancellationToken).ConfigureAwait(false);
        if (candidates.Length == 0)
        {
            return new Result(null, false, [], new ErrorInfo(
                "symbol_not_found",
                $"qualifiedName '{request.QualifiedName}' did not match any symbol.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["field"] = "qualifiedName",
                    ["provided"] = request.QualifiedName,
                    ["nextAction"] = "Refine qualifiedName or provide projectName/projectPath/projectId to narrow the lookup."
                })).WithWorkspaceRelativePaths(currentWorkspaceRoot);
        }

        if (candidates.Length > 1)
        {
            var qualifiedCandidates = candidates.Select(static candidate => candidate with { QualifiedDisplayName = candidate.QualifiedDisplayName ?? candidate.DisplayName }).ToArray();
            return new Result(null, true, qualifiedCandidates, new ErrorInfo(
                "ambiguous_symbol",
                $"qualifiedName '{request.QualifiedName}' matched multiple symbols.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["field"] = "qualifiedName",
                    ["provided"] = request.QualifiedName,
                    ["candidateCount"] = qualifiedCandidates.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["nextAction"] = "Select one candidate symbolId and call resolve_symbol again, or scope by projectName/projectPath/projectId."
                })).WithWorkspaceRelativePaths(currentWorkspaceRoot);
        }

        var selected = candidates[0];
        return new Result(new ResolvedSymbol(selected.SymbolId, selected.DisplayName, selected.Kind, selected.Location), false, []).WithWorkspaceRelativePaths(currentWorkspaceRoot);
    }

    private async Task<Result> ResolveBySymbolIdAsync(Request request, Microsoft.CodeAnalysis.Solution solution, string workspaceRoot, CancellationToken cancellationToken)
    {
        var symbol = await SymbolLookup.ResolveSymbolAsync(request.SymbolId!, solution, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return new Result(null, false, [], new ErrorInfo(
                "symbol_not_found",
                $"symbolId '{request.SymbolId}' could not be resolved.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["field"] = "symbolId",
                    ["provided"] = request.SymbolId!,
                    ["nextAction"] = "Call list_types/list_members or explain_symbol first to obtain a valid symbolId."
                })).WithWorkspaceRelativePaths(workspaceRoot);
        }

        return new Result(symbol.ToResolvedSymbol(), false, []).WithWorkspaceRelativePaths(workspaceRoot);
    }

    private async Task<Result> ResolveByPositionAsync(Request request, Microsoft.CodeAnalysis.Solution solution, string workspaceRoot, CancellationToken cancellationToken)
    {
        var symbol = await SymbolLookup.GetSymbolAtPositionAsync(solution, request.Path!, request.Line!.Value, request.Column!.Value, workspaceRoot, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return new Result(null, false, [], new ErrorInfo(
                "symbol_not_found",
                "No symbol found at the provided source position.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["field"] = "path",
                    ["provided"] = request.Path!,
                    ["nextAction"] = "Call resolve_symbol with a valid path+line+column or use list_types/list_members to select a symbolId."
                })).WithWorkspaceRelativePaths(workspaceRoot);
        }

        return new Result(symbol.ToResolvedSymbol(), false, []).WithWorkspaceRelativePaths(workspaceRoot);
    }
}
