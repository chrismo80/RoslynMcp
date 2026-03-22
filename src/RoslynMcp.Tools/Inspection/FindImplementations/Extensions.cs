using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Inspection.FindImplementations;

internal static class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddFindImplementationsTool() => services
			.AddSingleton<Service>()
			.AddSingleton<Tool>();
	}

	extension(ISymbol symbol)
	{
		internal CompactSymbol ToCompactSymbol()
		{
			var (filePath, line, column) = symbol.GetDeclarationPosition();
			return new CompactSymbol(
				symbol.ToStableId(),
				symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
				symbol.Kind.ToString(),
				CreateOptionalSourceLocation(filePath, line, column),
				symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
				?? symbol.ContainingNamespace.NormalizeNamespace());
		}

		internal async Task<IReadOnlyList<ISymbol>> FindImplementationSymbolsAsync(Solution solution, CancellationToken cancellationToken)
		{
			var projects = solution.Projects.ToImmutableHashSet();
			var unique = new Dictionary<string, ISymbol>(StringComparer.Ordinal);

			async Task AddAsync(IEnumerable<ISymbol> symbols)
			{
				foreach (var candidate in symbols)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var normalized = NormalizeResultSymbol(candidate);
					unique[normalized.ToStableId()] = normalized;
				}
			}

			var searchRoot = NormalizeSearchRoot(symbol);
			var implementations = await SymbolFinder.FindImplementationsAsync(searchRoot, solution, projects, cancellationToken).ConfigureAwait(false);
			await AddAsync(implementations).ConfigureAwait(false);

			if (searchRoot is IMethodSymbol or IPropertySymbol or IEventSymbol)
			{
				var overrides = await SymbolFinder.FindOverridesAsync(searchRoot, solution, projects, cancellationToken).ConfigureAwait(false);
				await AddAsync(overrides).ConfigureAwait(false);
			}

			return unique.Values
				.OrderBy(static candidate => candidate.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
				.ToArray();
		}

		private ISymbol NormalizeSearchRoot()
		{
			if (symbol is IMethodSymbol method)
			{
				if (method.ExplicitInterfaceImplementations.Length > 0)
					return method.ExplicitInterfaceImplementations[0].OriginalDefinition;

				if (method is { IsOverride: true, OverriddenMethod: not null })
					return method.OverriddenMethod.OriginalDefinition;
			}

			if (symbol is IPropertySymbol property)
			{
				if (property.ExplicitInterfaceImplementations.Length > 0)
					return property.ExplicitInterfaceImplementations[0].OriginalDefinition;

				if (property is { IsOverride: true, OverriddenProperty: not null })
					return property.OverriddenProperty.OriginalDefinition;
			}

			if (symbol is IEventSymbol eventSymbol)
			{
				if (eventSymbol.ExplicitInterfaceImplementations.Length > 0)
					return eventSymbol.ExplicitInterfaceImplementations[0].OriginalDefinition;

				if (eventSymbol.IsOverride && eventSymbol.OverriddenEvent is not null)
					return eventSymbol.OverriddenEvent.OriginalDefinition;
			}

			return symbol.OriginalDefinition ?? symbol;
		}

		private ISymbol NormalizeResultSymbol()
		{
			return symbol switch
			{
				IMethodSymbol method => method.ConstructedFrom ?? method.OriginalDefinition ?? method,
				IPropertySymbol property => property.OriginalDefinition ?? property,
				IEventSymbol eventSymbol => eventSymbol.OriginalDefinition ?? eventSymbol,
				INamedTypeSymbol namedType => namedType.OriginalDefinition ?? namedType,
				_ => symbol.OriginalDefinition ?? symbol
			};
		}
	}

	internal static Result WithWorkspaceRelativePaths(this Result result)
		=> result with
		{
			Symbol = result.Symbol.WithOptionalWorkspaceRelativePathValues(),
			Implementations = result.Implementations.Select(static implementation => implementation.WithWorkspaceRelativePathValues()).ToArray(),
			Error = result.Error.WithWorkspaceRelativePaths()
		};

	private static CompactSymbol? WithOptionalWorkspaceRelativePathValues(this CompactSymbol? symbol)
		=> symbol?.WithWorkspaceRelativePathValues();

	private static CompactSymbol WithWorkspaceRelativePathValues(this CompactSymbol symbol)
		=> symbol with { Location = symbol.Location.WithWorkspaceRelativePaths(), Owner = symbol.Owner?.Replace("global::", string.Empty, StringComparison.Ordinal) };

	private static SourceLocation? WithWorkspaceRelativePaths(this SourceLocation? location)
		=> location is null ? null : location with { FilePath = location.FilePath.ToWorkspaceRelativePathIfPossible() };

	private static ErrorInfo? WithWorkspaceRelativePaths(this ErrorInfo? error)
	{
		if (error?.Details is null || error.Details.Count == 0)
			return error;

		Dictionary<string, string>? updated = null;
		foreach (var pair in error.Details)
		{
			if (pair.Key is not ("path" or "filepath" or "provided" or "symbolId"))
				continue;

			var outward = pair.Key is "symbolId" ? pair.Value : pair.Value.ToWorkspaceRelativePathIfPossible();
			if (string.Equals(outward, pair.Value, StringComparison.Ordinal))
				continue;

			updated ??= new Dictionary<string, string>(error.Details, StringComparer.Ordinal);
			updated[pair.Key] = outward;
		}

		return updated is null ? error : error with { Details = updated };
	}

	private static SourceLocation? CreateOptionalSourceLocation(string filePath, int? line, int? column)
		=> string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue ? null : new(filePath, line.Value, column.Value);
}
