using RoslynMcp.Tools.Infrastructure.Services;

namespace RoslynMcp.Tools.Inspection.GetTypeHierarchy;

public sealed class Service(Workspace workspace, SymbolLookup symbolLookup)
{
	private const int DefaultMaxDerived = 200;

	public async Task<Result> RunAsync(string symbolId, bool includeTransitive, int maxDerived, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(symbolId))
		{
			return new Result(null, [], [], [], new ErrorInfo(
				"invalid_input",
				"symbolId must be provided.",
				new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["field"] = "symbolId",
					["nextAction"] = "Call get_type_hierarchy with a symbolId returned by resolve_symbol, list_types, or list_members."
				}));
		}

		var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
		if (session is null)
		{
			return new Result(null, [], [], [], new ErrorInfo(
				"no_solution_loaded",
				"No solution is currently loaded.",
				new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["nextAction"] = "Call load_solution first to select a solution before computing type hierarchies."
				}));
		}

		var symbol = await symbolLookup.ResolveSymbolAsync(symbolId, session.Solution, cancellationToken).ConfigureAwait(false);
		if (symbol is null)
		{
			return new Result(null, [], [], [], new ErrorInfo(
				"symbol_not_found",
				$"Symbol '{symbolId}' could not be resolved.",
				new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["symbolId"] = symbolId,
					["nextAction"] = "Call resolve_symbol first to obtain a valid symbolId."
				}));
		}

		var typeSymbol = symbol.GetRelatedType();
		if (typeSymbol is null)
		{
			return new Result(null, [], [], [], new ErrorInfo(
				"invalid_request",
				"symbolId must resolve to a type or a member declared on a type.",
				new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["parameter"] = "symbolId",
					["nextAction"] = "Provide a type symbolId or a member symbolId declared on a type."
				}));
		}

		var normalizedMaxDerived = maxDerived == default ? DefaultMaxDerived : Math.Max(maxDerived, 0);

		return new Result(
			typeSymbol.ToCompactSymbol(),
			typeSymbol.CollectBaseTypes(includeTransitive),
			typeSymbol.CollectImplementedInterfaces(includeTransitive),
			await typeSymbol.CollectDerivedTypesAsync(session.Solution, includeTransitive, normalizedMaxDerived, cancellationToken).ConfigureAwait(false)).WithWorkspaceRelativePaths();
	}
}
