using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Extensions;

internal static class SymbolExtensions
{
    extension(INamespaceSymbol namespaceSymbol)
    {
        internal IEnumerable<INamedTypeSymbol> GetTypes()
        {
            return namespaceSymbol
                .GetAllTypes()
                .Where(symbol => symbol.Locations.Any(location => location.IsInSource));
        }

        private IEnumerable<INamedTypeSymbol> GetAllTypes() => namespaceSymbol
            .GetTypeMembers()
            .Concat(namespaceSymbol.GetNamespaceMembers().SelectMany(GetAllTypes));
    }

    extension(ITypeSymbol symbol)
    {
        internal string ToTypeKind()
        {
            return symbol.IsRecord ? "record" : nameof(symbol.TypeKind).ToLower();
        }

        internal IReadOnlyList<string> MembersPreview(SymbolManager symbolManager, WorkspaceManager workspaceManager)
        {
            return symbol.GetMembers()
                .Where(m => m.DeclaredAccessibility > Accessibility.Private)
                .Select(m => MemberSymbol.From(m, symbolManager, workspaceManager))
                .Where(m => m.Kind != null)
                .Take(3)
                .OrderBy(m => m.Kind, StringComparer.Ordinal)
                .ThenBy(m => m.DisplayName, StringComparer.Ordinal)
                .Select(m => m.Text)
                .ToArray();
        }
        
        internal int MembersCount(SymbolManager symbolManager, WorkspaceManager workspaceManager)
        {
            return symbol.GetMembers()
                .Where(m => m.DeclaredAccessibility > Accessibility.Private)
                .Select(m => MemberSymbol.From(m, symbolManager, workspaceManager))
                .Count(m => m.Kind != null);
        }
    }

    extension(ISymbol symbol)
    {
        internal string? ToMemberKind()
        {
            return symbol switch
            {
                IPropertySymbol => "property",
                IFieldSymbol { IsImplicitlyDeclared: false } => "field",
                IEventSymbol => "event",
                IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "ctor",
                IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion or MethodKind.ReducedExtension or MethodKind.DelegateInvoke } => "method",
                _ => null
            };
        }

        internal string GetLocation(WorkspaceManager workspaceManager)
        {
            return string.Join(", ", symbol.Locations
                .Select(l => l.GetLineSpan())
                .Where(pos => pos.IsValid)
                .Select(pos => $"{workspaceManager.ToRelativePathIfPossible(pos.Path)}:{pos.StartLinePosition.Line + 1}"));
        }

        internal string ToLightweightMemberSignature() => symbol switch
        {
            IPropertySymbol property => $"{property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {property.Name} {{ {(property.GetMethod is null ? string.Empty : "get; ")}{(property.SetMethod is null ? string.Empty : property.SetMethod.IsInitOnly ? "init;" : "set;")} }}".Trim(),
            IFieldSymbol field => $"{field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {field.Name}",
            IEventSymbol @event => $"event {@event.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {@event.Name}",
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } ctor => $"{ctor.ContainingType.Name}({string.Join(", ", ctor.Parameters.Select(ToText))})",
            IMethodSymbol method => $"{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {method.Name}({string.Join(", ", method.Parameters.Select(ToText))})",
            _ => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        };
    }

    extension(IParameterSymbol parameter)
    {
        private string ToText()
        {
            var modifier = parameter switch
            {
                { IsParams: true } => "params ",
                { RefKind: RefKind.Ref } => "ref ",
                { RefKind: RefKind.Out } => "out ",
                { RefKind: RefKind.In } => "in ",
                _ => string.Empty
            };

            return $"{modifier}{parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {parameter.Name}";
        }
    }

    extension(Accessibility accessibility)
    {
        internal string ToText() => accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedAndInternal => "private_protected",
            Accessibility.ProtectedOrInternal => "protected_internal",
            _ => nameof(accessibility).ToLower()
        };
    }
}
