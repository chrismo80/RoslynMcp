using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Mutation;

namespace RoslynMcp.Tools.Mutation.RenameSymbol;

public sealed class Service(RoslynMcp.Tools.Infrastructure.Services.Workspace workspace)
{
    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
        => await RunAsync(request, allowReloadFallback: true, cancellationToken).ConfigureAwait(false);

    private async Task<Result> RunAsync(Request request, bool allowReloadFallback, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SymbolId) || string.IsNullOrWhiteSpace(request.NewName))
            return new Result(null, 0, [], [], new ErrorInfo("invalid_input", "symbolId and newName must be provided."));

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
            return new Result(null, 0, [], [], new ErrorInfo("no_solution_loaded", "No solution is currently loaded."));

        var symbol = await SymbolLookup.ResolveSymbolAsync(request.SymbolId, session.Solution, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            var aliasedSymbolId = await workspace.ResolveAliasedSymbolIdAsync(request.SymbolId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(aliasedSymbolId))
                symbol = await SymbolLookup.ResolveSymbolAsync(aliasedSymbolId, session.Solution, cancellationToken).ConfigureAwait(false);
        }

        if (symbol is null)
            return new Result(null, 0, [], [], new ErrorInfo("symbol_not_found", $"Symbol '{request.SymbolId}' could not be resolved."));

        if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(request.NewName))
            return new Result(null, 0, [], [], new ErrorInfo("invalid_new_name", $"'{request.NewName}' is not a valid identifier."));

        if (HasSimpleRenameConflict(symbol, request.NewName))
            return new Result(null, 0, [], [], new ErrorInfo("rename_conflict", $"Renaming '{symbol.Name}' to '{request.NewName}' would conflict with an existing symbol."));

        var references = await SymbolFinder.FindReferencesAsync(symbol, session.Solution, cancellationToken).ConfigureAwait(false);
        var affected = symbol.Locations
            .Where(static location => location.IsInSource)
            .Select(static location =>
            {
                var span = location.GetLineSpan();
                var start = span.StartLinePosition;
                return new { span.Path, Position = new ReferencePosition(start.Line + 1, start.Character + 1) };
            })
            .Concat(references
            .SelectMany(static reference => reference.Locations)
            .Where(static location => location.Location.IsInSource)
            .Select(static location =>
            {
                var span = location.Location.GetLineSpan();
                var start = span.StartLinePosition;
                return new { span.Path, Position = new ReferencePosition(start.Line + 1, start.Character + 1) };
            }))
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new AffectedFileLocations(group.Key, [.. group.Select(static item => item.Position).OrderBy(static position => position.Line).ThenBy(static position => position.Column)]))
            .OrderBy(static file => file.FilePath, StringComparer.Ordinal)
            .ToArray();

        var options = new SymbolRenameOptions(RenameOverloads: false, RenameInStrings: false, RenameInComments: false, RenameFile: false);
        var updatedSolution = await Renamer.RenameSymbolAsync(session.Solution, symbol, options, request.NewName, cancellationToken).ConfigureAwait(false);
        var changedFiles = await session.Solution.CollectChangedFilesAsync(updatedSolution, cancellationToken).ConfigureAwait(false);
        var renamedStableId = await ResolveRenamedStableIdAsync(symbol, updatedSolution, session.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!await workspace.ApplyChangesAsync(updatedSolution, cancellationToken).ConfigureAwait(false))
        {
            if (allowReloadFallback && await workspace.ReloadAsync(cancellationToken).ConfigureAwait(false))
                return await RunAsync(request, allowReloadFallback: false, cancellationToken).ConfigureAwait(false);

            return new Result(null, 0, [], changedFiles, new ErrorInfo("internal_error", "Failed to update the active solution after rename.")).WithWorkspaceRelativePaths();
        }

        if (!string.IsNullOrWhiteSpace(renamedStableId))
            await workspace.SetAliasedSymbolIdAsync(request.SymbolId, renamedStableId!, cancellationToken).ConfigureAwait(false);

        return new Result(request.SymbolId, changedFiles.Count, affected, changedFiles).WithWorkspaceRelativePaths();
    }

    private static bool HasSimpleRenameConflict(ISymbol symbol, string newName)
    {
        if (symbol is INamedTypeSymbol namedType)
            return namedType.ContainingNamespace.GetTypeMembers(newName).Any(candidate => !SymbolEqualityComparer.Default.Equals(candidate, namedType));

        if (symbol.ContainingType is not null)
            return symbol.ContainingType.GetMembers(newName).Any(candidate => !SymbolEqualityComparer.Default.Equals(candidate, symbol));

        return false;
    }

    private static async Task<string?> ResolveRenamedStableIdAsync(ISymbol symbol, Solution updatedSolution, string workspaceRoot, CancellationToken cancellationToken)
    {
        var (filePath, line, column) = symbol.GetDeclarationPosition();
        if (string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue)
            return null;

        var renamed = await SymbolLookup.GetSymbolAtPositionAsync(updatedSolution, filePath, line.Value, column.Value, workspaceRoot, cancellationToken).ConfigureAwait(false);
        return renamed?.ToStableId();
    }
}
