using RoslynMcp.Core.Models.Navigation;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Navigation;

public interface ITypeIntrospectionService
{
    INamedTypeSymbol? GetRelatedType(ISymbol symbol);
    IReadOnlyList<SymbolDescriptor> CollectBaseTypes(INamedTypeSymbol typeSymbol, bool includeTransitive);
    IReadOnlyList<SymbolDescriptor> CollectImplementedInterfaces(INamedTypeSymbol typeSymbol, bool includeTransitive);
    Task<IReadOnlyList<SymbolDescriptor>> CollectDerivedTypesAsync(
        INamedTypeSymbol typeSymbol,
        Solution solution,
        bool includeTransitive,
        int maxDerived,
        CancellationToken ct);
    IReadOnlyList<SymbolMemberOutline> CollectOutlineMembers(ISymbol symbol, int depth);
    IReadOnlyList<string> CollectAttributes(ISymbol symbol);
}
