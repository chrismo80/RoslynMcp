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
                symbol is INamedTypeSymbol namedType ? namedType.Name : symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
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

            return [.. result.Values
                .OrderBy(static symbol => symbol, TypeHierarchySymbolComparer.Instance)
                .Select(static symbol => symbol.ToCompactSymbol())];
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

            return [.. result.Values
                .OrderBy(static symbol => symbol, TypeHierarchySymbolComparer.Instance)
                .Select(static symbol => symbol.ToCompactSymbol())];
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

            return [.. unique.Values
                .OrderBy(static symbol => symbol, TypeHierarchySymbolComparer.Instance)
                .Take(maxDerived)
                .Select(static symbol => symbol.ToCompactSymbol())];

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
            BaseTypes = [.. result.BaseTypes.Select(static symbol => symbol.WithWorkspaceRelativePathValues())],
            ImplementedInterfaces = [.. result.ImplementedInterfaces.Select(static symbol => symbol.WithWorkspaceRelativePathValues())],
            DerivedTypes = [.. result.DerivedTypes.Select(static symbol => symbol.WithWorkspaceRelativePathValues())],
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

    private sealed class TypeHierarchySymbolComparer : IComparer<INamedTypeSymbol>
    {
        internal static readonly TypeHierarchySymbolComparer Instance = new();

        public int Compare(INamedTypeSymbol? x, INamedTypeSymbol? y)
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

            var byNamespace = StringComparer.Ordinal.Compare(x.ContainingNamespace.NormalizeNamespace() ?? string.Empty, y.ContainingNamespace.NormalizeNamespace() ?? string.Empty);
            if (byNamespace != 0)
                return byNamespace;

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
