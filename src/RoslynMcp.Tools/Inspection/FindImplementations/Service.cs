using RoslynMcp.Tools.Infrastructure.Services;

namespace RoslynMcp.Tools.Inspection.FindImplementations;

public sealed class Service(Workspace workspace)
{
    public async Task<Result> RunAsync(string symbolId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbolId))
        {
            return new Result(null, [], new ErrorInfo(
                "invalid_input",
                "symbolId must be provided.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["field"] = "symbolId",
                    ["nextAction"] = "Call find_implementations with a symbolId returned by resolve_symbol, list_types, or list_members."
                }));
        }

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return new Result(null, [], new ErrorInfo(
                "no_solution_loaded",
                "No solution is currently loaded.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nextAction"] = "Call load_solution first to select a solution before finding implementations."
                }));
        }

        var symbol = await SymbolLookup.ResolveSymbolAsync(symbolId, session.Solution, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return new Result(null, [], new ErrorInfo(
                "symbol_not_found",
                $"Symbol '{symbolId}' could not be resolved.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["symbolId"] = symbolId,
                    ["nextAction"] = "Call resolve_symbol first to obtain a valid symbolId."
                }));
        }

        var implementations = await symbol.FindImplementationSymbolsAsync(session.Solution, cancellationToken).ConfigureAwait(false);
        return new Result(symbol.ToCompactSymbol(), [.. implementations.Select(static implementation => implementation.ToCompactSymbol())]).WithWorkspaceRelativePaths();
    }
}
