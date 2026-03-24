using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Inspection.ListTypes;

internal static partial class Extensions
{
    internal static string ToText(this Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.Private => "private",
        Accessibility.ProtectedAndInternal => "private_protected",
        Accessibility.ProtectedOrInternal => "protected_internal",
        _ => "not_applicable"
    };

    extension(INamedTypeSymbol symbol)
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
    }

    extension(ISymbol symbol)
    {
        public string? ToMemberKind()
        {
            return symbol switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "ctor",
                IMethodSymbol method when method.MethodKind == MethodKind.Ordinary || method.MethodKind == MethodKind.UserDefinedOperator
                                                                                   || method.MethodKind == MethodKind.Conversion || method.MethodKind == MethodKind.ReducedExtension
                                                                                   || method.MethodKind == MethodKind.DelegateInvoke => "method",
                IPropertySymbol => "property",
                IFieldSymbol field when !field.IsImplicitlyDeclared => "field",
                IEventSymbol => "event",
                _ => null
            };
        }
    }

    internal static IReadOnlyList<string> GetDeclaredLightweightMembers(this INamedTypeSymbol type)
        => [.. type.GetMembers()
            .Where(member => member.DeclaredAccessibility > Accessibility.Private)
            .Select(member => new
            {
                Kind = member.ToMemberKind(),
                Entry = member.ToLightweightMemberEntry(),
                DisplayName = member.Kind == SymbolKind.Method && member is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor ? ctor.ContainingType.Name : member.Name,
                member.GetDeclarationPosition().FilePath,
                Signature = member.ToLightweightMemberSignature(),
                Member = member
            })
            .Where(static item => item.Kind is not null)
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Signature, StringComparer.Ordinal)
            .ThenBy(static item => item.Member.ToStableId(), StringComparer.Ordinal)
            .Select(static item => item.Entry!)];

    extension(ISymbol member)
    {
        internal string? ToLightweightMemberEntry() =>
            member.ToMemberKind() is null ? null : $"{member.ToStableId()}: {member.DeclaredAccessibility.ToText()} {member.ToLightweightMemberSignature()}";

        internal string ToLightweightMemberSignature() => member switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } ctor => $"{ctor.ContainingType.Name}({string.Join(", ", ctor.Parameters.Select(FormatParameter))})",
                IMethodSymbol method => $"{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {method.Name}({string.Join(", ", method.Parameters.Select(FormatParameter))})",
                IPropertySymbol property => $"{property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {property.Name} {{ {(property.GetMethod is null ? string.Empty : "get; ")}{(property.SetMethod is null ? string.Empty : property.SetMethod.IsInitOnly ? "init;" : "set;")} }}".Trim(),
                IFieldSymbol field => $"{field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {field.Name}",
                IEventSymbol @event => $"event {@event.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {@event.Name}",
                _ => member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            };
    }

    private static string FormatParameter(IParameterSymbol parameter)
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

    internal static IEnumerable<INamedTypeSymbol> EnumerateTypes(this INamespaceSymbol root)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();

        foreach (var member in root.GetMembers().OrderBy(static member => member.Name, StringComparer.Ordinal))
            stack.Push(member);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            switch (current)
            {
                case INamedTypeSymbol namedType:
                    yield return namedType;
                    foreach (var nested in namedType.GetTypeMembers().OrderByDescending(static member => member.Name, StringComparer.Ordinal))
                        stack.Push(nested);
                    break;
                case INamespaceSymbol ns:
                    foreach (var member in ns.GetMembers().OrderByDescending(static member => member.Name, StringComparer.Ordinal))
                        stack.Push(member);
                    break;
            }
        }
    }

    extension(ErrorInfo? error)
    {
        private ErrorInfo? WithWorkspaceRelativePaths()
        {
            if (error?.Details is null || error.Details.Count == 0)
                return error;

            Dictionary<string, string>? updated = null;
            foreach (var pair in error.Details)
            {
                if (pair.Key is not ("path" or "filepath" or "projectpath" or "provided"))
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
}