using RoslynMcp.Core.Models.Navigation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Collections.Immutable;

namespace RoslynMcp.Infrastructure.Navigation;

internal sealed class TypeIntrospectionService : ITypeIntrospectionService
{
    public INamedTypeSymbol? GetRelatedType(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol namedType)
        {
            return namedType.OriginalDefinition;
        }

        return symbol.ContainingType?.OriginalDefinition;
    }

    public IReadOnlyList<SymbolDescriptor> CollectBaseTypes(INamedTypeSymbol typeSymbol, bool includeTransitive)
        => CollectBaseTypeSymbols(typeSymbol, includeTransitive)
            .Select(NavigationModelUtilities.CreateDescriptor)
            .OrderBy(static descriptor => descriptor, SymbolDescriptorComparer.Instance)
            .ToArray();

    public IReadOnlyList<SymbolDescriptor> CollectImplementedInterfaces(INamedTypeSymbol typeSymbol, bool includeTransitive)
        => CollectImplementedInterfaceSymbols(typeSymbol, includeTransitive)
            .Select(NavigationModelUtilities.CreateDescriptor)
            .OrderBy(static descriptor => descriptor, SymbolDescriptorComparer.Instance)
            .ToArray();

    public async Task<IReadOnlyList<SymbolDescriptor>> CollectDerivedTypesAsync(INamedTypeSymbol typeSymbol,
        Solution solution,
        bool includeTransitive,
        int maxDerived,
        CancellationToken ct)
    {
        if (maxDerived == 0)
        {
            return Array.Empty<SymbolDescriptor>();
        }

        var unique = new Dictionary<string, SymbolDescriptor>(StringComparer.Ordinal);
        var rootId = SymbolIdentity.CreateId(typeSymbol.OriginalDefinition);
        var projects = solution.Projects.ToImmutableHashSet();

        if (typeSymbol.TypeKind == TypeKind.Interface)
        {
            var derivedInterfaces = await SymbolFinder.FindDerivedInterfacesAsync(typeSymbol, solution, includeTransitive, projects, ct).ConfigureAwait(false);
            AddDerived(derivedInterfaces, unique, maxDerived, rootId, includeTransitive);

            var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, solution, projects, ct).ConfigureAwait(false);
            AddDerived(implementations.OfType<INamedTypeSymbol>(), unique, maxDerived, rootId, includeTransitive);
        }
        else
        {
            var derivedClasses = await SymbolFinder.FindDerivedClassesAsync(typeSymbol, solution, includeTransitive, projects, ct).ConfigureAwait(false);
            AddDerived(derivedClasses, unique, maxDerived, rootId, includeTransitive);
        }

        return unique.Values
            .OrderBy(static descriptor => descriptor, SymbolDescriptorComparer.Instance)
            .Take(maxDerived)
            .ToArray();
    }

    public IReadOnlyList<SymbolMemberOutline> CollectOutlineMembers(ISymbol symbol, int depth)
    {
        if (symbol is not INamedTypeSymbol rootType)
        {
            return Array.Empty<SymbolMemberOutline>();
        }

        var queue = new Queue<(INamedTypeSymbol Type, int Level)>();
        var members = new Dictionary<string, SymbolMemberOutline>(StringComparer.Ordinal);
        queue.Enqueue((rootType.OriginalDefinition, 1));

        while (queue.Count > 0)
        {
            var (type, level) = queue.Dequeue();

            foreach (var member in type.GetMembers())
            {
                if (member.IsImplicitlyDeclared)
                {
                    continue;
                }

                if (member is IMethodSymbol method &&
                    (method.MethodKind == MethodKind.PropertyGet || method.MethodKind == MethodKind.PropertySet ||
                     method.MethodKind == MethodKind.EventAdd || method.MethodKind == MethodKind.EventRemove))
                {
                    continue;
                }

                var outline = new SymbolMemberOutline(
                    member.Name,
                    member.Kind.ToString(),
                    member.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    member.DeclaredAccessibility.ToString(),
                    member.IsStatic);

                members[NavigationModelUtilities.GetOutlineMemberKey(outline)] = outline;

                if (level < depth && member is INamedTypeSymbol nestedType)
                {
                    queue.Enqueue((nestedType.OriginalDefinition, level + 1));
                }
            }
        }

        return members.Values.OrderBy(static member => member, SymbolMemberOutlineComparer.Instance).ToArray();
    }

    public IReadOnlyList<string> CollectAttributes(ISymbol symbol)
        => symbol.GetAttributes()
            .Select(static attr => attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<INamedTypeSymbol> CollectBaseTypeSymbols(INamedTypeSymbol typeSymbol, bool includeTransitive)
    {
        var result = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var current = typeSymbol.BaseType;

        while (current != null)
        {
            var normalized = current.OriginalDefinition;
            result[SymbolIdentity.CreateId(normalized)] = normalized;
            if (!includeTransitive)
            {
                break;
            }

            current = normalized.BaseType;
        }

        return result.Values.ToArray();
    }

    private static IReadOnlyList<INamedTypeSymbol> CollectImplementedInterfaceSymbols(INamedTypeSymbol typeSymbol, bool includeTransitive)
    {
        var interfaces = includeTransitive ? typeSymbol.AllInterfaces : typeSymbol.Interfaces;
        var result = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

        foreach (var iface in interfaces)
        {
            var normalized = iface.OriginalDefinition;
            result[SymbolIdentity.CreateId(normalized)] = normalized;
        }

        return result.Values.ToArray();
    }

    private static void AddDerived(IEnumerable<INamedTypeSymbol> symbols,
        IDictionary<string, SymbolDescriptor> unique,
        int maxDerived,
        string rootId,
        bool includeTransitive)
    {
        foreach (var symbol in symbols)
        {
            if (unique.Count >= maxDerived)
            {
                return;
            }

            if (!includeTransitive)
            {
                var normalizedCandidate = symbol.OriginalDefinition;
                var directBaseMatch = normalizedCandidate.BaseType != null &&
                                      string.Equals(SymbolIdentity.CreateId(normalizedCandidate.BaseType.OriginalDefinition), rootId, StringComparison.Ordinal);
                var directInterfaceMatch = normalizedCandidate.Interfaces.Any(iface =>
                    string.Equals(SymbolIdentity.CreateId(iface.OriginalDefinition), rootId, StringComparison.Ordinal));

                if (!directBaseMatch && !directInterfaceMatch)
                {
                    continue;
                }
            }

            var normalized = symbol.OriginalDefinition;
            var id = SymbolIdentity.CreateId(normalized);
            unique[id] = NavigationModelUtilities.CreateDescriptor(normalized);
        }
    }
}
