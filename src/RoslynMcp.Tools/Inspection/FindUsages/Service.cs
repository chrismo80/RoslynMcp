using RoslynMcp.Tools.Infrastructure.Services;

namespace RoslynMcp.Tools.Inspection.FindUsages;

public sealed class Service(Workspace workspace)
{
    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SymbolId))
        {
            return new Result(null, [], 0, new ErrorInfo(
                "invalid_input",
                "symbolId must be provided.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["field"] = "symbolId",
                    ["nextAction"] = "Call find_usages with a symbolId returned by resolve_symbol, list_types, or list_members."
                }));
        }

        if (!request.Scope.IsValidScope())
        {
            return new Result(null, [], 0, new ErrorInfo(
                "invalid_request",
                "scope must be one of: document, project, solution.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["parameter"] = "scope",
                    ["provided"] = request.Scope,
                    ["nextAction"] = "Call find_usages with scope=document, scope=project, or scope=solution."
                }));
        }

        if (string.Equals(request.Scope, ReferenceScopes.Document, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(request.Path))
        {
            return new Result(null, [], 0, new ErrorInfo(
                "invalid_request",
                "path is required when scope is document.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["parameter"] = "path",
                    ["nextAction"] = "Call find_usages with a document path when scope=document."
                }));
        }

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return new Result(null, [], 0, new ErrorInfo(
                "no_solution_loaded",
                "No solution is currently loaded.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nextAction"] = "Call load_solution first to select a solution before finding usages."
                }));
        }

        if (string.Equals(request.Scope, ReferenceScopes.Document, StringComparison.Ordinal))
        {
            var absolutePath = request.Path!.ToWorkspaceAbsolutePath();
            var exists = session.Solution.Projects.SelectMany(static project => project.Documents).Any(document => document.FilePath.MatchesByNormalizedPath(absolutePath));
            if (!exists)
            {
                return new Result(null, [], 0, new ErrorInfo(
                    "invalid_path",
                    $"Document path '{request.Path}' is not part of the selected solution.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["path"] = request.Path!,
                        ["nextAction"] = "Provide a document path that exists in the loaded solution."
                    })).WithWorkspaceRelativePaths();
            }
        }

        var (symbol, ownerProject) = await SymbolLookup.ResolveSymbolWithProjectAsync(request.SymbolId, session.Solution, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return new Result(null, [], 0, new ErrorInfo(
                "symbol_not_found",
                $"Symbol '{request.SymbolId}' could not be resolved.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["symbolId"] = request.SymbolId,
                    ["nextAction"] = "Call resolve_symbol first to obtain a valid symbolId."
                }));
        }

        var absolutePathForScope = request.Path?.ToWorkspaceAbsolutePath();
        var references = await symbol.FindReferencesScopedAsync(session.Solution, request.Scope, absolutePathForScope, ownerProject, cancellationToken).ConfigureAwait(false);

        return new Result(
            new UsageSymbol(symbol.ToStableId(), symbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.MinimallyQualifiedFormat), symbol.Kind.ToString(), CreateOptionalSourceLocation(symbol)),
            [.. references.GroupBy(static reference => reference.FilePath, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(group => new ReferenceFileGroup(group.Key, [.. group.Select(static reference => new ReferencePosition(reference.Line, reference.Column))]))],
            references.Count).WithWorkspaceRelativePaths();
    }

    private static SourceLocation? CreateOptionalSourceLocation(Microsoft.CodeAnalysis.ISymbol symbol)
    {
        var (filePath, line, column) = symbol.GetDeclarationPosition();
        return string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue ? null : new SourceLocation(filePath, line.Value, column.Value);
    }
}
