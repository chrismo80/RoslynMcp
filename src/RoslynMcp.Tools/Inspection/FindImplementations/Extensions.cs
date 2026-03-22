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

            return [.. unique.Values.OrderBy(static candidate => candidate, ImplementationSymbolComparer.Instance)];
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
            Implementations = [.. result.Implementations.Select(static implementation => implementation.WithWorkspaceRelativePathValues())],
            Error = result.Error.WithWorkspaceRelativePaths()
        };

    private static CompactSymbol? WithOptionalWorkspaceRelativePathValues(this CompactSymbol? symbol)
        => symbol?.WithWorkspaceRelativePathValues();

    private static CompactSymbol WithWorkspaceRelativePathValues(this CompactSymbol symbol)
        => symbol with { Location = symbol.Location.WithWorkspaceRelativePaths() };

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

    private sealed class ImplementationSymbolComparer : IComparer<ISymbol>
    {
        internal static readonly ImplementationSymbolComparer Instance = new();

        public int Compare(ISymbol? x, ISymbol? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x is null)
                return -1;

            if (y is null)
                return 1;

            var byNameIgnoreCase = StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
            if (byNameIgnoreCase != 0)
                return byNameIgnoreCase;

            var byName = StringComparer.Ordinal.Compare(x.Name, y.Name);
            if (byName != 0)
                return byName;

            var byKind = StringComparer.Ordinal.Compare(x.Kind.ToString(), y.Kind.ToString());
            if (byKind != 0)
                return byKind;

            var byNamespace = StringComparer.Ordinal.Compare(x.ContainingNamespace.NormalizeNamespace() ?? string.Empty, y.ContainingNamespace.NormalizeNamespace() ?? string.Empty);
            if (byNamespace != 0)
                return byNamespace;

            var byType = StringComparer.Ordinal.Compare(x.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty, y.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty);
            if (byType != 0)
                return byType;

            var (xPath, xLine, xColumn) = x.GetDeclarationPosition();
            var (yPath, yLine, yColumn) = y.GetDeclarationPosition();

            var byPath = StringComparer.Ordinal.Compare(xPath, yPath);
            if (byPath != 0)
                return byPath;

            var byLine = Nullable.Compare(xLine, yLine);
            if (byLine != 0)
                return byLine;

            var byColumn = Nullable.Compare(xColumn, yColumn);
            if (byColumn != 0)
                return byColumn;

            return StringComparer.Ordinal.Compare(x.ToStableId(), y.ToStableId());
        }
    }
}
