using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Inspection.FindUsages;

internal static class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddFindUsagesTool() => services
			.AddSingleton<Service>()
			.AddSingleton<Tool>();
	}

	internal static string NormalizeScope(this string? scope)
	{
		var normalized = string.IsNullOrWhiteSpace(scope) ? ReferenceScopes.Solution : scope.Trim().ToLowerInvariant();
		return normalized is ReferenceScopes.Document or ReferenceScopes.Project or ReferenceScopes.Solution ? normalized : normalized;
	}

	internal static bool IsValidScope(this string scope)
		=> scope is ReferenceScopes.Document or ReferenceScopes.Project or ReferenceScopes.Solution;

	internal static async Task<IReadOnlyList<SourceLocation>> FindReferencesScopedAsync(this ISymbol symbol, Solution solution, string scope, string? path, Project? ownerProject, CancellationToken cancellationToken)
	{
		var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
		var unique = new HashSet<string>(StringComparer.Ordinal);
		var locations = new List<SourceLocation>();

		foreach (var reference in references)
		{
			cancellationToken.ThrowIfCancellationRequested();

			foreach (var location in reference.Locations)
			{
				if (!location.Location.IsInSource || !location.IsInScope(scope, path, ownerProject))
					continue;

				var source = location.Location.ToSourceLocation();
				var key = $"{source.FilePath}:{source.Line}:{source.Column}";
				if (unique.Add(key))
					locations.Add(source);
			}
		}

		return locations
			.OrderBy(static location => location.FilePath, StringComparer.Ordinal)
			.ThenBy(static location => location.Line)
			.ThenBy(static location => location.Column)
			.ToArray();
	}

	private static bool IsInScope(this ReferenceLocation referenceLocation, string scope, string? path, Project? ownerProject)
	{
		var document = referenceLocation.Document;

		if (document is null)
			return false;

		if (string.Equals(scope, ReferenceScopes.Solution, StringComparison.Ordinal))
			return true;

		if (string.Equals(scope, ReferenceScopes.Project, StringComparison.Ordinal))
			return ownerProject is not null && document.Project.Id == ownerProject.Id;

		return string.Equals(scope, ReferenceScopes.Document, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(path)
			&& document.FilePath.MatchesByNormalizedPath(path);
	}

	internal static SourceLocation ToSourceLocation(this Location location)
	{
		var span = location.GetLineSpan();
		var start = span.StartLinePosition;
		return new SourceLocation(span.Path ?? string.Empty, start.Line + 1, start.Character + 1);
	}

	internal static Result WithWorkspaceRelativePaths(this Result result)
		=> result with
		{
			Symbol = result.Symbol.WithWorkspaceRelativePaths(),
			ReferenceFiles = result.ReferenceFiles.Select(static group => group.WithWorkspaceRelativePaths()).ToArray(),
			Error = result.Error.WithWorkspaceRelativePaths()
		};

	private static UsageSymbol? WithWorkspaceRelativePaths(this UsageSymbol? symbol)
		=> symbol is null ? null : symbol with { Location = symbol.Location.WithWorkspaceRelativePaths() };

	private static ReferenceFileGroup WithWorkspaceRelativePaths(this ReferenceFileGroup group)
		=> group with { FilePath = group.FilePath.ToWorkspaceRelativePathIfPossible() };

	private static SourceLocation? WithWorkspaceRelativePaths(this SourceLocation? location)
		=> location is null ? null : location with { FilePath = location.FilePath.ToWorkspaceRelativePathIfPossible() };

	private static ErrorInfo? WithWorkspaceRelativePaths(this ErrorInfo? error)
	{
		if (error?.Details is null || error.Details.Count == 0)
			return error;

		Dictionary<string, string>? updated = null;
		foreach (var pair in error.Details)
		{
			if (pair.Key is not ("path" or "filepath" or "provided"))
				continue;

			var outward = pair.Value.ToWorkspaceRelativePathIfPossible();
			if (string.Equals(outward, pair.Value, StringComparison.Ordinal))
				continue;

			updated ??= new Dictionary<string, string>(error.Details, StringComparer.Ordinal);
			updated[pair.Key] = outward;
		}

		return updated is null ? error : error with { Details = updated };
	}
}