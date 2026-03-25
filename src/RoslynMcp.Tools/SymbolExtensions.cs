using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools;

internal static class SymbolExtensions
{
    extension(INamespaceSymbol namespaceSymbol)
    {
        internal IEnumerable<INamedTypeSymbol> GetTypes()
        {
            return namespaceSymbol.GetNamespaceMembers()
                .SelectMany(GetAllTypes)
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
            if (symbol.IsRecord)
                return "record";

            return symbol.TypeKind switch
            {
                TypeKind.Class => "class",
                TypeKind.Interface => "interface",
                TypeKind.Enum => "enum",
                TypeKind.Struct => "struct",
                _ => "unknown"
            };
        }

        internal IReadOnlyList<string> MembersPreview(SymbolManager symbolManager, WorkspaceManager workspaceManager)
        {
            return symbol.GetMembers()
                .Where(m => m.DeclaredAccessibility > Accessibility.Private)
                .Select(m => MemberSymbol.From(m, symbolManager, workspaceManager))
                .Where(m => m.Kind != null)
                .Take(10)
                .OrderBy(m => m.Kind, StringComparer.Ordinal)
                .ThenBy(m => m.DisplayName, StringComparer.Ordinal)
                .Select(m => m.Text)
                .ToArray();
        }
    }

    extension(ISymbol symbol)
    {
        internal string? ToMemberKind()
        {
            return symbol switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "ctor",
                IMethodSymbol method when method.MethodKind == MethodKind.Ordinary || method.MethodKind == MethodKind.UserDefinedOperator
                    || method.MethodKind == MethodKind.Conversion || method.MethodKind == MethodKind.ReducedExtension
                    || method.MethodKind == MethodKind.DelegateInvoke => "method",
                IPropertySymbol => "property",
                IFieldSymbol { IsImplicitlyDeclared: false } => "field",
                IEventSymbol => "event",
                _ => null
            };
        }

        internal string? GetLocation(WorkspaceManager workspaceManager)
        {
            var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);

            if (location is null)
                return null;

            var span = location.GetLineSpan();
            var start = span.StartLinePosition;

            return workspaceManager.ToRelativePathIfPossible(span.Path);
        }

        internal string ToLightweightMemberSignature() => symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } ctor => $"{ctor.ContainingType.Name}({string.Join(", ", ctor.Parameters.Select(ToText))})",
            IMethodSymbol method => $"{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {method.Name}({string.Join(", ", method.Parameters.Select(ToText))})",
            IPropertySymbol property => $"{property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {property.Name} {{ {(property.GetMethod is null ? string.Empty : "get; ")}{(property.SetMethod is null ? string.Empty : property.SetMethod.IsInitOnly ? "init;" : "set;")} }}".Trim(),
            IFieldSymbol field => $"{field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {field.Name}",
            IEventSymbol @event => $"event {@event.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {@event.Name}",
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
            _ => "not_applicable"
        };
    }
}