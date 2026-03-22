using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Inspection.GetTypeHierarchy;

internal static class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddGetTypeHierarchyTool() => services
			.AddSingleton<Service>()
			.AddSingleton<Tool>();
	}

	extension(ISymbol symbol)
	{
		internal INamedTypeSymbol? GetRelatedType()
		{
			if (symbol is INamedTypeSymbol namedType)
				return namedType.OriginalDefinition;

			return symbol.ContainingType?.OriginalDefinition;
		}

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
	}

	extension(INamedTypeSymbol typeSymbol)
	{
		internal IReadOnlyList<CompactSymbol> CollectBaseTypes(bool includeTransitive)
		{
			var result = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
			var current = typeSymbol.BaseType;

			while (current is not null)
			{
				var normalized = current.OriginalDefinition;
				result[normalized.ToStableId()] = normalized;

				if (!includeTransitive)
					break;

				current = normalized.BaseType;
			}

			return result.Values
				.OrderBy(static symbol => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
				.Select(static symbol => symbol.ToCompactSymbol())
				.ToArray();
		}

		internal IReadOnlyList<CompactSymbol> CollectImplementedInterfaces(bool includeTransitive)
		{
			var interfaces = includeTransitive ? typeSymbol.AllInterfaces : typeSymbol.Interfaces;
			var result = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

			foreach (var iface in interfaces)
			{
				var normalized = iface.OriginalDefinition;
				result[normalized.ToStableId()] = normalized;
			}

			return result.Values
				.OrderBy(static symbol => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
				.Select(static symbol => symbol.ToCompactSymbol())
				.ToArray();
		}

		internal async Task<IReadOnlyList<CompactSymbol>> CollectDerivedTypesAsync(Solution solution, bool includeTransitive, int maxDerived, CancellationToken cancellationToken)
		{
			if (maxDerived == 0)
				return [];

			var unique = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
			var rootId = typeSymbol.OriginalDefinition.ToStableId();
			var projects = solution.Projects.ToImmutableHashSet();

			if (typeSymbol.TypeKind == TypeKind.Interface)
			{
				var derivedInterfaces = await SymbolFinder.FindDerivedInterfacesAsync(typeSymbol, solution, includeTransitive, projects, cancellationToken).ConfigureAwait(false);
				AddDerived(derivedInterfaces);

				var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, solution, projects, cancellationToken).ConfigureAwait(false);
				AddDerived(implementations.OfType<INamedTypeSymbol>());
			}
			else
			{
				var derivedClasses = await SymbolFinder.FindDerivedClassesAsync(typeSymbol, solution, includeTransitive, projects, cancellationToken).ConfigureAwait(false);
				AddDerived(derivedClasses);
			}

			return unique.Values
				.OrderBy(static symbol => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
				.Take(maxDerived)
				.Select(static symbol => symbol.ToCompactSymbol())
				.ToArray();

			void AddDerived(IEnumerable<INamedTypeSymbol> symbols)
			{
				foreach (var symbol in symbols)
				{
					if (unique.Count >= maxDerived)
						return;

					var normalized = symbol.OriginalDefinition;

					if (!includeTransitive)
					{
						var directBaseMatch = normalized.BaseType is not null && string.Equals(normalized.BaseType.OriginalDefinition.ToStableId(), rootId, StringComparison.Ordinal);
						var directInterfaceMatch = normalized.Interfaces.Any(iface => string.Equals(iface.OriginalDefinition.ToStableId(), rootId, StringComparison.Ordinal));

						if (!directBaseMatch && !directInterfaceMatch)
							continue;
					}

					unique[normalized.ToStableId()] = normalized;
				}
			}
		}
	}

	internal static Result WithWorkspaceRelativePaths(this Result result)
		=> result with
		{
			Symbol = result.Symbol.WithOptionalWorkspaceRelativePathValues(),
			BaseTypes = result.BaseTypes.Select(static symbol => symbol.WithWorkspaceRelativePathValues()).ToArray(),
			ImplementedInterfaces = result.ImplementedInterfaces.Select(static symbol => symbol.WithWorkspaceRelativePathValues()).ToArray(),
			DerivedTypes = result.DerivedTypes.Select(static symbol => symbol.WithWorkspaceRelativePathValues()).ToArray(),
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
