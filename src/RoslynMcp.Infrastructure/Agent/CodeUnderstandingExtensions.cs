using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Navigation;

namespace RoslynMcp.Infrastructure.Agent;

public static partial class CodeUnderstandingExtensions
{
    public static IEnumerable<INamedTypeSymbol> EnumerateTypes(this INamespaceSymbol root)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();

        foreach (var member in root.GetMembers().OrderBy(static m => m.Name, StringComparer.Ordinal))
            stack.Push(member);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            switch (current)
            {
                case INamedTypeSymbol namedType:
                {
                    yield return namedType;

                    foreach (var nested in namedType.GetTypeMembers().OrderByDescending(static m => m.Name, StringComparer.Ordinal))
                        stack.Push(nested);

                    continue;
                }
                case INamespaceSymbol ns:
                {
                    foreach (var member in ns.GetMembers().OrderByDescending(static m => m.Name, StringComparer.Ordinal))
                        stack.Push(member);
                    break;
                }
            }
        }
    }

    public static ImmutableArray<ISymbol> CollectMembersWithInheritance(this INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        static IEnumerable<INamedTypeSymbol> Traverse(INamedTypeSymbol current)
        {
            yield return current;

            var baseType = current.BaseType;

            while (baseType != null)
            {
                yield return baseType;

                baseType = baseType.BaseType;
            }

            foreach (var iface in current.AllInterfaces.OrderBy(static i => i.ToDisplayString(), StringComparer.Ordinal))
                yield return iface;
        }

        foreach (var declaringType in Traverse(type))
        {
            foreach (var member in declaringType.GetMembers())
            {
                var kind = member.ToMemberKind();

                if (kind == null)
                    continue;

                var key = member.CreateId();

                if (seen.Add(key))
                    builder.Add(member);
            }
        }

        return builder.ToImmutable();
    }

    extension(ISymbol member)
    {
        public string? ToMemberKind()
        {
            return member switch
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

        public MemberListEntry? ToMemberEntry(string? normalizedKind,
            string? normalizedAccessibility,
            string? normalizedBinding)
        {
            var memberKind = member.ToMemberKind();
            if (memberKind == null)
                return null;

            if (normalizedKind != null && !string.Equals(memberKind, normalizedKind, StringComparison.Ordinal))
                return null;

            var accessibility = member.DeclaredAccessibility.NormalizeAccessibility();
            if (normalizedAccessibility != null && !string.Equals(accessibility, normalizedAccessibility, StringComparison.Ordinal))
                return null;

            if (normalizedBinding != null)
            {
                var isStatic = member.IsStatic;
                if ((string.Equals(normalizedBinding, "static", StringComparison.Ordinal) && !isStatic)
                    || (string.Equals(normalizedBinding, "instance", StringComparison.Ordinal) && isStatic))
                {
                    return null;
                }
            }

            var (filePath, line, column) = member.GetDeclarationPosition();
            return new MemberListEntry(
                member.Kind == SymbolKind.Method && member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                    ? constructor.ContainingType.Name
                    : member.Name,
                member.CreateId().ToExternal(),
                memberKind,
                member.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.MinimallyQualifiedFormat),
                CreateOptionalSourceLocation(filePath, line, column),
                accessibility,
                member.IsStatic);
        }

        public string? ToLightweightMemberEntry()
        {
            if (member.ToMemberKind() == null)
                return null;

            return $"{member.CreateId().ToExternal()}: {member.DeclaredAccessibility.NormalizeAccessibility()} {member.ToLightweightMemberSignature()}";
        }

        public string ToLightweightMemberSignature() => member switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } constructor
                => $"{constructor.ContainingType.ToReadableTypeName()}({FormatParameters(constructor.Parameters)})",
            IMethodSymbol method
                => FormatMethodSignature(method),
            IPropertySymbol property
                => $"{property.Type.ToReadableTypeReference(includeNamespaces: false)} {FormatPropertyName(property)} {{ {FormatPropertyAccessors(property)} }}",
            IFieldSymbol field
                => $"{field.Type.ToReadableTypeReference(includeNamespaces: false)} {field.Name}",
            IEventSymbol @event
                => $"event {@event.Type.ToReadableTypeReference(includeNamespaces: false)} {@event.Name}",
            _ => member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        };
    }

    private static string FormatMethodSignature(IMethodSymbol method)
    {
        if (method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion)
            return method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        var typeParameters = method.TypeParameters.Length == 0 ? string.Empty :
            $"<{string.Join(", ", method.TypeParameters.Select(static parameter => parameter.Name))}>";

        return $"{method.ReturnType.ToReadableTypeReference(includeNamespaces: false)} {method.Name}{typeParameters}({FormatParameters(method.Parameters)})";
    }

    private static string FormatParameters(ImmutableArray<IParameterSymbol> parameters)
        => string.Join(", ", parameters.Select(FormatParameter));

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

        return $"{modifier}{parameter.Type.ToReadableTypeReference(includeNamespaces: false)} {parameter.Name}";
    }

    private static string FormatPropertyName(IPropertySymbol property)
        => property.IsIndexer
            ? $"this[{FormatParameters(property.Parameters)}]"
            : property.Name;

    private static string FormatPropertyAccessors(IPropertySymbol property)
    {
        var accessors = new List<string>(2);
        if (property.GetMethod != null)
            accessors.Add("get;");

        if (property.SetMethod != null)
            accessors.Add(property.SetMethod.IsInitOnly ? "init;" : "set;");

        return string.Join(" ", accessors);
    }

    extension(INamedTypeSymbol symbol)
    {
        public bool IsPartial()
        {
            foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax typeDeclaration && typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
                    return true;
            }

            return false;
        }

        public string? ToTypeKind()
        {
            if (symbol.IsRecord)
                return "record";

            return symbol.TypeKind switch
            {
                TypeKind.Class => "class",
                TypeKind.Interface => "interface",
                TypeKind.Enum => "enum",
                TypeKind.Struct => "struct",
                _ => null
            };
        }

        public string ToReadableQualifiedTypeName()
        {
            var segments = new List<string>();
            var ns = symbol.ContainingNamespace.NormalizeNamespace();
            if (!string.IsNullOrEmpty(ns))
                segments.Add(ns);

            var typeStack = new Stack<INamedTypeSymbol>();
            for (var current = symbol; current != null; current = current.ContainingType)
                typeStack.Push(current);

            while (typeStack.Count > 0)
                segments.Add(typeStack.Pop().ToReadableTypeName());

            return string.Join(".", segments);
        }
    }

    public static (string FilePath, int? Line, int? Column) GetDeclarationPosition(this ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static l => l.IsInSource);
        if (location == null)
            return (string.Empty, null, null);

        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        return (span.Path ?? string.Empty, start.Line + 1, start.Character + 1);
    }

    public static (string FilePath, int? StartLine, int? StartColumn, int? EndLine, int? EndColumn) GetSourceSpan(this ISymbol? symbol)
    {
        if (symbol?.Locations.FirstOrDefault(static l => l.IsInSource) is not { } location)
            return (string.Empty, null, null, null, null);

        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;
        return (span.Path ?? string.Empty, start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1);
    }

    public static string NormalizeAccessibility(this Accessibility accessibility)
    {
        return accessibility switch
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

    public static string NormalizeNamespace(this INamespaceSymbol? ns)
    {
        if (ns?.IsGlobalNamespace != false)
            return string.Empty;

        return ns.ToDisplayString();
    }

    public static string NormalizeQualifiedName(this string value)
        => value.Trim().Replace("global::", string.Empty, StringComparison.Ordinal);

    public static bool LooksLikeShortNameQuery(this string normalizedQualifiedName)
        => normalizedQualifiedName.IndexOf('.') < 0;

    public static bool MatchesQualifiedName(this ISymbol symbol, string normalizedQualifiedName)
    {
        var full = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).NormalizeQualifiedName();
        if (string.Equals(full, normalizedQualifiedName, StringComparison.Ordinal))
        {
            return true;
        }

        var csharpError = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat).NormalizeQualifiedName();
        return string.Equals(csharpError, normalizedQualifiedName, StringComparison.Ordinal);
    }

    public static string RemoveAllWhitespace(this string value)
        => string.Concat(value.Where(static ch => !char.IsWhiteSpace(ch)));

    internal static IReadOnlyList<QualifiedNameSegment> GetQualifiedTypeSegments(this INamedTypeSymbol symbol, bool includeSelf)
    {
        var segments = new Stack<QualifiedNameSegment>();

        var currentType = includeSelf ? symbol : symbol.ContainingType;
        while (currentType != null)
        {
            segments.Push(new QualifiedNameSegment(currentType.Name, currentType.Arity > 0 ? currentType.Arity : null));
            currentType = currentType.ContainingType;
        }

        var namespaceSymbol = symbol.ContainingNamespace;
        while (namespaceSymbol is { IsGlobalNamespace: false })
        {
            segments.Push(new QualifiedNameSegment(namespaceSymbol.Name, null));
            namespaceSymbol = namespaceSymbol.ContainingNamespace;
        }

        return segments.ToArray();
    }

    internal static IReadOnlyList<QualifiedNameSegment> GetQualifiedContainerSegments(this ISymbol symbol)
    {
        if (symbol.ContainingType != null)
            return symbol.ContainingType.GetQualifiedTypeSegments(includeSelf: true);

        var segments = new Stack<QualifiedNameSegment>();
        var namespaceSymbol = symbol.ContainingNamespace;
        while (namespaceSymbol is { IsGlobalNamespace: false })
        {
            segments.Push(new QualifiedNameSegment(namespaceSymbol.Name, null));
            namespaceSymbol = namespaceSymbol.ContainingNamespace;
        }

        return segments.ToArray();
    }

    public static IEnumerable<string> GetComparableTypeNames(this ITypeSymbol type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            type.ToReadableTypeReference(includeNamespaces: false).NormalizeQualifiedName().RemoveAllWhitespace(),
            type.ToReadableTypeReference(includeNamespaces: true).NormalizeQualifiedName().RemoveAllWhitespace()
        };

        return names;
    }

    extension(ISymbol symbol)
    {
        public string ToReadableHandle() => $"{symbol.GetReadableHandlePrefix()}:{symbol.ToQualifiedDisplayName()}";

        public string ToQualifiedDisplayName()
        {
            switch (symbol)
            {
                case INamedTypeSymbol namedType:
                    return namedType.ToReadableQualifiedTypeName();
                case IMethodSymbol method:
                    {
                        var container = method.ContainingType?.ToReadableQualifiedTypeName() ?? method.ContainingNamespace.NormalizeNamespace();
                        var methodName = method.MethodKind == MethodKind.Constructor
                            ? method.ContainingType?.Name ?? method.Name
                            : method.Name;
                        var parameters = string.Join(", ", method.Parameters.Select(static parameter => parameter.Type.ToReadableTypeReference(includeNamespaces: false)));
                        return $"{container}.{methodName}({parameters})";
                    }
            }

            if (symbol.ContainingType != null)
                return $"{symbol.ContainingType.ToReadableQualifiedTypeName()}.{symbol.Name}";

            var ns = symbol.ContainingNamespace.NormalizeNamespace();

            return string.IsNullOrEmpty(ns) ? symbol.Name : $"{ns}.{symbol.Name}";
        }

        public SymbolReference ToSymbolReference()
        {
            var (filePath, line, column) = symbol.GetDeclarationPosition();
            var internalSymbolId = symbol.CreateId();
            return new SymbolReference(
                internalSymbolId.ToExternal(),
                symbol.ToReadableHandle(),
                symbol.ToQualifiedDisplayName(),
                CreateOptionalSourceLocation(filePath, line, column));
        }

        private string GetReadableHandlePrefix()
            => symbol switch
            {
                INamedTypeSymbol => "type",
                IMethodSymbol { MethodKind: MethodKind.Constructor } => "ctor",
                IMethodSymbol => "method",
                IPropertySymbol => "property",
                IFieldSymbol => "field",
                IEventSymbol => "event",
                _ => symbol.Kind.ToString().ToLowerInvariant()
            };

        public ResolvedSymbolSummary ToResolvedSymbol(bool includeQualifiedDisplayName = false)
        {
            var (filePath, line, column) = symbol.GetDeclarationPosition();
            return new ResolvedSymbolSummary(
                symbol.CreateId().ToExternal(),
                symbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.MinimallyQualifiedFormat),
                symbol.Kind.ToString(),
                CreateOptionalSourceLocation(filePath, line, column),
                includeQualifiedDisplayName ? symbol.ToQualifiedDisplayName() : null);
        }

        public CompactSymbolSummary ToCompactSummary()
        {
            var (filePath, line, column) = symbol.GetDeclarationPosition();
            return new CompactSymbolSummary(
                symbol.CreateId().ToExternal(),
                symbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.MinimallyQualifiedFormat),
                symbol.Kind.ToString(),
                CreateOptionalSourceLocation(filePath, line, column),
                symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                ?? symbol.ContainingNamespace.NormalizeNamespace().NormalizeOptional());
        }
    }

    public static string ToReadableTypeReference(this ITypeSymbol type, bool includeNamespaces)
        => type.ToDisplayString(CreateReadableTypeFormat(includeNamespaces));

    public static string ToReadableTypeName(this INamedTypeSymbol type)
    {
        if (type.Arity == 0)
            return type.Name;

        var typeParameters = type.TypeParameters.Length > 0
            ? type.TypeParameters.Select(static parameter => parameter.Name)
            : type.TypeArguments.Select(static argument => argument.ToReadableTypeReference(includeNamespaces: false));
        return $"{type.Name}<{string.Join(", ", typeParameters)}>";
    }

    public static SymbolReference CreateSymbolReference(
        string symbolId,
        string handle,
        string qualifiedDisplayName,
        string filePath,
        int? line,
        int? column)
        => new(symbolId, handle, qualifiedDisplayName, CreateOptionalSourceLocation(filePath, line, column));

    private static SymbolDisplayFormat CreateReadableTypeFormat(bool includeNamespaces)
        => new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: includeNamespaces
                ? SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                : SymbolDisplayTypeQualificationStyle.NameOnly,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static SourceLocation? CreateOptionalSourceLocation(string filePath, int? line, int? column)
        => string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue
            ? null
            : new SourceLocation(filePath, line.Value, column.Value);

    extension(IReadOnlyList<DiagnosticItem> diagnostics)
    {
        public DiagnosticsSummary ToDiagnosticsSummary()
        {
            var error = diagnostics.Count(static d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase));
            var warning = diagnostics.Count(static d => string.Equals(d.Severity, "warning", StringComparison.OrdinalIgnoreCase));
            var info = diagnostics.Count - error - warning;
            return new DiagnosticsSummary(error, warning, info, diagnostics.Count);
        }

        public DiagnosticsSummary ToLoadBaselineDiagnosticsSummary()
        {
            var filtered = diagnostics
                .Where(static diagnostic => SourceVisibility.ClassifyPath(diagnostic.Location.FilePath) is SourceKind.HandWritten or SourceKind.Unknown)
                .ToArray();

            return filtered.ToDiagnosticsSummary();
        }
    }
}
