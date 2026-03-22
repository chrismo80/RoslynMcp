namespace RoslynMcp.Tools.Inspection.ResolveSymbols;

public sealed class Service(ResolveSymbol.Service resolveSymbol)
{
	private const int MaximumEntries = 100;

	public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Entries.Count == 0)
			return CreateValidationError("entries must contain at least one selector.", ("field", "entries"));

		if (request.Entries.Count > MaximumEntries)
		{
			return CreateValidationError(
				$"entries cannot contain more than {MaximumEntries} selectors.",
				("field", "entries"),
				("provided", request.Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
		}

		var results = new List<ItemResult>(request.Entries.Count);

		for (var index = 0; index < request.Entries.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var entry = request.Entries[index];
			var resolved = await resolveSymbol.RunAsync(entry.ToResolveSymbolRequest(), cancellationToken).ConfigureAwait(false);
			results.Add(resolved.ToItemResult(index, entry.Label));
		}

		return new Result(
			results,
			results.Count,
			results.Count(static item => item.Symbol is not null),
			results.Count(static item => item.IsAmbiguous),
			results.Count(static item => item.Error is not null));
	}

	private static Result CreateValidationError(string message, params (string Key, string? Value)[] details)
		=> new([], 0, 0, 0, 0, new ErrorInfo(
			"invalid_input",
			message,
			details.Where(static detail => detail.Value is not null)
				.ToDictionary(static detail => detail.Key, static detail => detail.Value!, StringComparer.Ordinal)
				.Append(new KeyValuePair<string, string>("nextAction", "Call resolve_symbols with 1-100 selector entries."))
				.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)));
}
