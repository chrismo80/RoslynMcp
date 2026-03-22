using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Inspection.ListMembers;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddListMembersTool() => services
            .AddSingleton<Service>()
            .AddSingleton<Tool>();
    }

    extension(string? typeSymbolId)
    {
        public Request ToRequest(
            string? path,
            int? line,
            int? column,
            string? kind,
            string? accessibility,
            string? binding,
            bool? includeInherited,
            int? limit,
            int? offset)
            => new(
                typeSymbolId.NormalizeOptional(),
                path.NormalizeOptional(),
                line.NormalizePosition(),
                column.NormalizePosition(),
                kind.NormalizeOptional()?.ToLowerInvariant(),
                accessibility.NormalizeOptional()?.ToLowerInvariant(),
                binding.NormalizeOptional()?.ToLowerInvariant(),
                includeInherited ?? false,
                limit.NormalizeNonNegative(),
                offset.NormalizeNonNegative());
    }

    internal static bool TryNormalizeMemberKind(this string? kind, out string? normalized)
    {
        normalized = kind.NormalizeOptional()?.ToLowerInvariant();

        switch (normalized)
        {
            case null:
            case "method" or "property" or "field" or "event" or "ctor":
                return true;
            default:
                normalized = null;
                return false;
        }
    }

    internal static bool TryNormalizeBinding(this string? binding, out string? normalized)
    {
        normalized = binding.NormalizeOptional()?.ToLowerInvariant();

        switch (normalized)
        {
            case null:
            case "static" or "instance":
                return true;
            default:
                normalized = null;
                return false;
        }
    }

    internal static bool TryNormalizeAccessibility(this string? accessibility, out string? normalized)
    {
        normalized = accessibility.NormalizeOptional()?.Replace('-', '_').ToLowerInvariant();

        switch (normalized)
        {
            case null:
            case "public" or "internal" or "protected" or "private" or "protected_internal" or "private_protected":
                return true;
            default:
                normalized = null;
                return false;
        }
    }

    internal static int? NormalizePosition(this int? value) => value.HasValue ? Math.Max(value.Value, 0) : null;

    internal static int? NormalizeNonNegative(this int? value) => value.HasValue ? Math.Max(value.Value, 0) : null;

    internal static (int Offset, int Limit) NormalizePaging(this int? offset, int? limit)
    {
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var normalizedLimit = limit.HasValue ? Math.Clamp(limit.Value, 0, 500) : 100;
        return (normalizedOffset, normalizedLimit);
    }

    internal static string NormalizeAccessibility(this Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.Private => "private",
        Accessibility.ProtectedAndInternal => "private_protected",
        Accessibility.ProtectedOrInternal => "protected_internal",
        _ => "not_applicable"
    };

    internal static ImmutableArray<ISymbol> CollectMembersWithInheritance(this INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var declaringType in Traverse(type))
        {
            foreach (var member in declaringType.GetMembers())
            {
                var kind = member.ToMemberKind();
                if (kind is null)
                    continue;

                if (seen.Add(member.ToStableId()))
                    builder.Add(member);
            }
        }

        return builder.ToImmutable();
    }

    extension(ISymbol member)
    {
        internal string? ToMemberKind() => member switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "ctor",
            IMethodSymbol method when method.MethodKind == MethodKind.Ordinary || method.MethodKind == MethodKind.UserDefinedOperator || method.MethodKind == MethodKind.Conversion || method.MethodKind == MethodKind.ReducedExtension || method.MethodKind == MethodKind.DelegateInvoke => "method",
            IPropertySymbol => "property",
            IFieldSymbol field when !field.IsImplicitlyDeclared => "field",
            IEventSymbol => "event",
            _ => null
        };

        internal Entry? ToEntry(string? normalizedKind, string? normalizedAccessibility, string? normalizedBinding)
        {
            var memberKind = member.ToMemberKind();
            if (memberKind is null)
                return null;

            if (normalizedKind is not null && !string.Equals(memberKind, normalizedKind, StringComparison.Ordinal))
                return null;

            var accessibility = member.DeclaredAccessibility.NormalizeAccessibility();
            if (normalizedAccessibility is not null && !string.Equals(accessibility, normalizedAccessibility, StringComparison.Ordinal))
                return null;

            if (normalizedBinding is not null)
            {
                var isStatic = member.IsStatic;
                if ((string.Equals(normalizedBinding, "static", StringComparison.Ordinal) && !isStatic)
                    || (string.Equals(normalizedBinding, "instance", StringComparison.Ordinal) && isStatic))
                {
                    return null;
                }
            }

            var (filePath, line, column) = member.GetDeclarationPosition();
            return new Entry(
                member.Kind == SymbolKind.Method && member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                    ? constructor.ContainingType.Name
                    : member.Name,
                member.ToStableId(),
                memberKind,
                member.ToMemberSignature(),
                CreateOptionalSourceLocation(filePath, line, column),
                accessibility,
                member.IsStatic);
        }

        private string ToMemberSignature() => member switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } constructor => $"{constructor.ContainingType.Name}({string.Join(", ", constructor.Parameters.Select(FormatParameter))})",
            IMethodSymbol method when method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion => method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IMethodSymbol method => $"{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {method.Name}{method.FormatTypeParameters()}({string.Join(", ", method.Parameters.Select(FormatParameter))})",
            IPropertySymbol property => $"{property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {property.FormatPropertyName()} {{ {property.FormatPropertyAccessors()} }}",
            IFieldSymbol field => $"{field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {field.Name}",
            IEventSymbol @event => $"event {@event.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {@event.Name}",
            _ => member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        };
    }

    extension(IMethodSymbol method)
    {
        private string FormatTypeParameters()
            => method.TypeParameters.Length == 0
                ? string.Empty
                : $"<{string.Join(", ", method.TypeParameters.Select(static parameter => parameter.Name))}>";
    }

    extension(IPropertySymbol property)
    {
        private string FormatPropertyName()
            => property.IsIndexer ? $"this[{string.Join(", ", property.Parameters.Select(FormatParameter))}]" : property.Name;

        private string FormatPropertyAccessors()
        {
            var accessors = new List<string>(2);
            if (property.GetMethod is not null)
                accessors.Add("get;");

            if (property.SetMethod is not null)
                accessors.Add(property.SetMethod.IsInitOnly ? "init;" : "set;");

            return string.Join(" ", accessors);
        }
    }

    private static IEnumerable<INamedTypeSymbol> Traverse(INamedTypeSymbol type)
    {
        yield return type;

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
            yield return baseType;

        foreach (var iface in type.AllInterfaces.OrderBy(static iface => iface.ToDisplayString(), StringComparer.Ordinal))
            yield return iface;
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

    private static SourceLocation? CreateOptionalSourceLocation(string filePath, int? line, int? column)
        => string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue
            ? null
            : new SourceLocation(filePath, line.Value, column.Value);

    extension(Result result)
    {
        internal Result WithWorkspaceRelativePaths()
            => result with
            {
                Members = [.. result.Members.Select(member => member.WithWorkspaceRelativePaths())],
                Error = result.Error.WithWorkspaceRelativePaths()
            };
    }

    extension(Entry entry)
    {
        private Entry WithWorkspaceRelativePaths()
            => entry with { Location = entry.Location.WithWorkspaceRelativePaths() };
    }

    extension(SourceLocation? location)
    {
        private SourceLocation? WithWorkspaceRelativePaths()
            => location is null ? null : location with { FilePath = location.FilePath.ToWorkspaceRelativePathIfPossible() };
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
                if (pair.Key is not ("path" or "filepath" or "provided"))
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
