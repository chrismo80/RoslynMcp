using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Infrastructure;

namespace RoslynMcp.Tools.Inspection.ListTypes;

internal static partial class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddListTypesTool() => services
            .AddSingleton<Service>()
            .AddSingleton<Tool>();
    }

    extension(string? projectPath)
    {
        public Request ToRequest(
            string? projectName,
            string? projectId,
            string? namespacePrefix,
            string? kind,
            string? accessibility,
            bool? includeSummary,
            bool? includeMembers,
            int? limit,
            int? offset)
            => new(
                projectPath.NormalizeOptional(),
                projectName.NormalizeOptional(),
                projectId.NormalizeOptional(),
                namespacePrefix.NormalizeOptional(),
                kind.NormalizeOptional(),
                accessibility.NormalizeOptional(),
                includeSummary ?? true,
                includeMembers ?? false,
                limit,
                offset);
    }

    internal static bool TryNormalizeTypeKind(this string? kind, out string? normalized)
    {
        normalized = kind.NormalizeOptional()?.ToLowerInvariant();

        switch (normalized)
        {
            case null:
            case "class" or "record" or "interface" or "enum" or "struct":
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

    internal static string NormalizeNamespace(this INamespaceSymbol? ns)
        => ns?.IsGlobalNamespace != false ? string.Empty : ns.ToDisplayString();

    internal static string? ToTypeKind(this INamedTypeSymbol symbol)
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

    internal static Entry Enrich(this Discovery discovery, bool includeSummary, bool includeMembers)
    {
        var summary = includeSummary ? discovery.Symbol.GetDocumentation()?.Summary : discovery.Entry.Summary;
        var members = includeMembers ? discovery.Symbol.GetDeclaredLightweightMembers() : discovery.Entry.Members;

        return discovery.Entry with { Summary = summary, Members = members };
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
            .Where(static item => item.Kind is not null && SourceVisibility.ShouldIncludeInHumanResults(item.FilePath))
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Signature, StringComparer.Ordinal)
            .ThenBy(static item => item.Member.ToStableId(), StringComparer.Ordinal)
            .Select(static item => item.Entry!)];

    extension(ISymbol member)
    {
        private string? ToMemberKind()
            => member switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "ctor",
                IMethodSymbol method when method.MethodKind == MethodKind.Ordinary || method.MethodKind == MethodKind.UserDefinedOperator || method.MethodKind == MethodKind.Conversion || method.MethodKind == MethodKind.ReducedExtension || method.MethodKind == MethodKind.DelegateInvoke => "method",
                IPropertySymbol => "property",
                IFieldSymbol field when !field.IsImplicitlyDeclared => "field",
                IEventSymbol => "event",
                _ => null
            };

        private string? ToLightweightMemberEntry()
            => member.ToMemberKind() is null ? null : $"{member.ToStableId()}: {member.DeclaredAccessibility.NormalizeAccessibility()} {member.ToLightweightMemberSignature()}";

        private string ToLightweightMemberSignature()
            => member switch
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

    extension(IEnumerable<string?> paths)
    {
        internal VisibilityAssessment AssessPaths()
        {
            var handwritten = 0;
            var generated = 0;
            var unknown = 0;

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    unknown++;
                    continue;
                }

                if (SourceVisibility.IsGeneratedLike(path))
                    generated++;
                else
                    handwritten++;
            }

            var visibility = handwritten > 0 && generated > 0 ? SourceBiases.Mixed : handwritten > 0 ? SourceBiases.Handwritten : generated > 0 ? SourceBiases.Generated : SourceBiases.Unknown;

            return new VisibilityAssessment(visibility, handwritten, generated, unknown);
        }
    }

    extension(Result result)
    {
        internal Result WithWorkspaceRelativePaths()
            => result with
            {
                Types = [.. result.Types.Select(type => type.WithWorkspaceRelativePaths())],
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

    internal static SymbolDocumentation? GetDocumentation(this ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: CancellationToken.None);
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        XElement root;
        try
        {
            root = XElement.Parse($"<root>{xml}</root>", LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }

        var summary = NormalizeElementText(root.Descendants("summary").FirstOrDefault());
        var returns = NormalizeElementText(root.Descendants("returns").FirstOrDefault());
        var parameters = root.Descendants("param")
            .Select(CreateParameterDocumentation)
            .Where(static parameter => parameter is not null)
            .Cast<SymbolParameterDocumentation>()
            .ToArray();

        return summary is null && returns is null && parameters.Length == 0 ? null : new SymbolDocumentation(summary, returns, parameters);
    }

    private static SymbolParameterDocumentation? CreateParameterDocumentation(XElement element)
    {
        var name = NormalizeText(element.Attribute("name")?.Value);
        var description = NormalizeElementText(element);
        return name is null || description is null ? null : new SymbolParameterDocumentation(name, description);
    }

    private static string? NormalizeElementText(XElement? element)
    {
        if (element is null)
            return null;

        var builder = new StringBuilder();
        AppendNodeText(element, builder);
        return NormalizeText(builder.ToString());
    }

    private static void AppendNodeText(XNode node, StringBuilder builder)
    {
        switch (node)
        {
            case XText text:
                builder.Append(text.Value);
                return;
            case XElement element when element.Name.LocalName is "see" or "seealso":
                builder.Append(NormalizeSymbolReference(element.Attribute("cref")?.Value));
                return;
            case XElement element when element.Name.LocalName is "paramref" or "typeparamref":
                builder.Append(element.Attribute("name")?.Value);
                return;
            case XElement element:
                foreach (var child in element.Nodes())
                    AppendNodeText(child, builder);
                return;
        }
    }

    private static string? NormalizeSymbolReference(string? cref)
    {
        var normalized = NormalizeText(cref);
        if (normalized is null)
            return null;

        return normalized.Length > 2 && normalized[1] == ':' ? normalized[2..] : normalized;
    }

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : WhitespacePattern().Replace(value.Trim(), " ");

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespacePattern();
}
