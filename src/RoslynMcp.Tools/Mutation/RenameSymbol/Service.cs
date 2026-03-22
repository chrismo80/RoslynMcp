using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Mutation;

namespace RoslynMcp.Tools.Mutation.RenameSymbol;

public sealed class Service(RoslynMcp.Tools.Infrastructure.Services.Workspace workspace)
{
    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SymbolId) || string.IsNullOrWhiteSpace(request.NewName))
            return new Result(null, 0, [], [], new ErrorInfo("invalid_input", "symbolId and newName must be provided."));

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
            return new Result(null, 0, [], [], new ErrorInfo("no_solution_loaded", "No solution is currently loaded."));

        var symbol = await SymbolLookup.ResolveSymbolAsync(request.SymbolId, session.Solution, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
            return new Result(null, 0, [], [], new ErrorInfo("symbol_not_found", $"Symbol '{request.SymbolId}' could not be resolved."));

        if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(request.NewName))
            return new Result(null, 0, [], [], new ErrorInfo("invalid_new_name", $"'{request.NewName}' is not a valid identifier."));

        var references = await SymbolFinder.FindReferencesAsync(symbol, session.Solution, cancellationToken).ConfigureAwait(false);
        var affected = references
            .SelectMany(static reference => reference.Locations)
            .Where(static location => location.Location.IsInSource)
            .Select(static location =>
            {
                var span = location.Location.GetLineSpan();
                var start = span.StartLinePosition;
                return new { span.Path, Position = new ReferencePosition(start.Line + 1, start.Character + 1) };
            })
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new AffectedFileLocations(group.Key, [.. group.Select(static item => item.Position).OrderBy(static position => position.Line).ThenBy(static position => position.Column)]))
            .OrderBy(static file => file.FilePath, StringComparer.Ordinal)
            .ToArray();

        var options = new SymbolRenameOptions(RenameOverloads: false, RenameInStrings: false, RenameInComments: false, RenameFile: false);
        var updatedSolution = await Renamer.RenameSymbolAsync(session.Solution, symbol, options, request.NewName, cancellationToken).ConfigureAwait(false);
        var changedFiles = await session.Solution.CollectChangedFilesAsync(updatedSolution, cancellationToken).ConfigureAwait(false);
        if (!session.Workspace.TryApplyChanges(updatedSolution))
            return new Result(null, 0, [], changedFiles, new ErrorInfo("internal_error", "Failed to update the active solution after rename.")).WithWorkspaceRelativePaths();

        return new Result(request.SymbolId, changedFiles.Count, affected, changedFiles).WithWorkspaceRelativePaths();
    }
}
