using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Infrastructure;

namespace RoslynMcp.Tools.Inspection.ResolveSymbol;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddResolveSymbolTool() => services
            .AddSingleton<Service>()
            .AddSingleton<Tool>();
    }

    extension(string? symbolId)
    {
        public Request ToRequest(
            string? path,
            int? line,
            int? column,
            string? qualifiedName,
            string? projectPath,
            string? projectName,
            string? projectId)
            => new(
                symbolId.NormalizeOptional(),
                path.NormalizeOptional(),
                line.NormalizePosition(),
                column.NormalizePosition(),
                qualifiedName.NormalizeOptional(),
                projectPath.NormalizeOptional(),
                projectName.NormalizeOptional(),
                projectId.NormalizeOptional());
    }

    internal static int? NormalizePosition(this int? value) => value.HasValue ? Math.Max(value.Value, 0) : null;

    internal static string NormalizeQualifiedName(this string value)
        => value.Trim().Replace("global::", string.Empty, StringComparison.Ordinal);

    internal static string RemoveAllWhitespace(this string value)
        => string.Concat(value.Where(static character => !char.IsWhiteSpace(character)));

    internal static async Task<Candidate[]> ResolveByQualifiedNameAsync(this string qualifiedName, IReadOnlyList<Project> projects, CancellationToken cancellationToken)
    {
        var normalizedQualifiedName = qualifiedName.NormalizeQualifiedName();
        var query = QualifiedSymbolQuery.Parse(normalizedQualifiedName);
        if (string.IsNullOrWhiteSpace(query.LookupName))
            return [];

        var candidates = new List<(ISymbol Symbol, string ProjectName)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in projects.OrderBy(static project => project.Name, StringComparer.Ordinal))
        {
            var symbols = await SymbolFinder.FindDeclarationsAsync(project, query.LookupName, ignoreCase: false, SymbolFilter.TypeAndMember, cancellationToken).ConfigureAwait(false);

            foreach (var symbol in symbols)
            {
                var normalizedSymbol = symbol.OriginalDefinition ?? symbol;
                if (!seen.Add(normalizedSymbol.ToStableId()))
                    continue;

				candidates.Add((normalizedSymbol, project.Name));
            }
        }

        var strictMatches = candidates.Where(match => query.Matches(match.Symbol)).ToArray();
        if (strictMatches.Length > 0)
            return OrderCandidates(strictMatches, query.LookupName);

        if (!query.IsShortNameOnly)
            return [];

        var caseSensitiveMatches = candidates.Where(match => string.Equals(match.Symbol.Name, query.LookupName, StringComparison.Ordinal)).ToArray();
        var shortNameMatches = caseSensitiveMatches.Length > 0
            ? caseSensitiveMatches
            : [.. candidates.Where(match => string.Equals(match.Symbol.Name, query.LookupName, StringComparison.OrdinalIgnoreCase))];

        return OrderCandidates(shortNameMatches, query.LookupName);
    }

    private static Candidate[] OrderCandidates((ISymbol Symbol, string ProjectName)[] matches, string lookupName)
        => [.. matches
            .OrderByDescending(match => string.Equals(match.Symbol.Name, lookupName, StringComparison.Ordinal))
            .ThenBy(match => match.Symbol.Kind.ToString(), StringComparer.Ordinal)
            .ThenBy(match => match.Symbol.ToQualifiedDisplayName(), StringComparer.Ordinal)
            .ThenBy(match => match.ProjectName, StringComparer.Ordinal)
            .ThenBy(match => match.Symbol.ToStableId(), StringComparer.Ordinal)
            .Select(static match => match.Symbol.ToCandidate(match.ProjectName))];

    extension(ISymbol symbol)
    {
        internal bool MatchesQualifiedName(string normalizedQualifiedName)
        {
            var fullyQualified = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).NormalizeQualifiedName();
            if (string.Equals(fullyQualified, normalizedQualifiedName, StringComparison.Ordinal))
                return true;

            var csharpError = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat).NormalizeQualifiedName();
            return string.Equals(csharpError, normalizedQualifiedName, StringComparison.Ordinal);
        }

        internal string ToQualifiedDisplayName()
        {
            return symbol switch
            {
                INamedTypeSymbol namedType => namedType.ToReadableQualifiedTypeName(),
                IMethodSymbol method => method.ToQualifiedMethodDisplayName(),
                _ when symbol.ContainingType is not null => $"{symbol.ContainingType.ToReadableQualifiedTypeName()}.{symbol.Name}",
                _ => string.IsNullOrEmpty(symbol.ContainingNamespace?.ToDisplayString()) ? symbol.Name : $"{symbol.ContainingNamespace.ToDisplayString()}.{symbol.Name}"
            };
        }

        internal ResolvedSymbol ToResolvedSymbol(bool includeQualifiedDisplayName = false)
        {
            var (filePath, line, column) = symbol.GetDeclarationPosition();
            return new ResolvedSymbol(
                symbol.ToStableId(),
                symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                symbol.Kind.ToString(),
                CreateOptionalSourceLocation(filePath, line, column),
                includeQualifiedDisplayName ? symbol.ToQualifiedDisplayName() : null);
        }

        internal Candidate ToCandidate(string projectName)
        {
            var resolved = symbol.ToResolvedSymbol(includeQualifiedDisplayName: true);
            return new Candidate(resolved.SymbolId, resolved.DisplayName, resolved.Kind, resolved.Location, projectName, resolved.QualifiedDisplayName);
        }

	}

    extension(IMethodSymbol method)
    {
        private string ToQualifiedMethodDisplayName()
        {
            var container = method.ContainingType?.ToReadableQualifiedTypeName() ?? method.ContainingNamespace.NormalizeNamespace();
            var methodName = method.MethodKind == MethodKind.Constructor ? method.ContainingType?.Name ?? method.Name : method.Name;
            var parameters = string.Join(", ", method.Parameters.Select(static parameter => parameter.Type.ToReadableTypeReference(includeNamespaces: false)));
            return $"{container}.{methodName}({parameters})";
        }
    }

    internal static string ToReadableQualifiedTypeName(this INamedTypeSymbol symbol)
    {
        var segments = new List<string>();
        var ns = symbol.ContainingNamespace.NormalizeNamespace();
        if (!string.IsNullOrEmpty(ns))
            segments.Add(ns);

        var stack = new Stack<INamedTypeSymbol>();
        for (var current = symbol; current is not null; current = current.ContainingType)
            stack.Push(current);

        while (stack.Count > 0)
            segments.Add(stack.Pop().ToReadableTypeName());

        return string.Join('.', segments);
    }

    internal static IReadOnlyList<QualifiedNameSegment> GetQualifiedTypeSegments(this INamedTypeSymbol symbol, bool includeSelf)
    {
        var segments = new Stack<QualifiedNameSegment>();

        for (var current = includeSelf ? symbol : symbol.ContainingType; current is not null; current = current.ContainingType)
            segments.Push(new QualifiedNameSegment(current.Name, current.Arity > 0 ? current.Arity : null));

        for (var ns = symbol.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            segments.Push(new QualifiedNameSegment(ns.Name, null));

        return [.. segments];
    }

    internal static string ToReadableTypeName(this INamedTypeSymbol symbol)
    {
        if (symbol.Arity == 0)
            return symbol.Name;

        var typeParameters = symbol.TypeParameters.Length > 0
            ? symbol.TypeParameters.Select(static parameter => parameter.Name)
            : symbol.TypeArguments.Select(static argument => argument.ToReadableTypeReference(includeNamespaces: false));
        return $"{symbol.Name}<{string.Join(", ", typeParameters)}>";
    }

    internal static IReadOnlyList<QualifiedNameSegment> GetQualifiedContainerSegments(this ISymbol symbol)
    {
        if (symbol.ContainingType is not null)
            return symbol.ContainingType.GetQualifiedTypeSegments(includeSelf: true);

        var segments = new Stack<QualifiedNameSegment>();
        for (var ns = symbol.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            segments.Push(new QualifiedNameSegment(ns.Name, null));

        return [.. segments];
    }

    internal static string NormalizeNamespace(this INamespaceSymbol? ns)
        => ns?.IsGlobalNamespace != false ? string.Empty : ns.ToDisplayString();

    internal static IEnumerable<string> GetComparableTypeNames(this ITypeSymbol type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            type.ToReadableTypeReference(includeNamespaces: false).NormalizeQualifiedName().RemoveAllWhitespace(),
            type.ToReadableTypeReference(includeNamespaces: true).NormalizeQualifiedName().RemoveAllWhitespace()
        };

        return names;
    }

    internal static string ToReadableTypeReference(this ITypeSymbol type, bool includeNamespaces)
        => type.ToDisplayString(CreateReadableTypeFormat(includeNamespaces));

    private static SymbolDisplayFormat CreateReadableTypeFormat(bool includeNamespaces)
        => new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: includeNamespaces ? SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces : SymbolDisplayTypeQualificationStyle.NameOnly,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static SourceLocation? CreateOptionalSourceLocation(string filePath, int? line, int? column)
        => string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue ? null : new SourceLocation(filePath, line.Value, column.Value);

    extension(Result result)
    {
        internal Result WithWorkspaceRelativePaths()
            => result with
            {
                Symbol = result.Symbol.WithWorkspaceRelativePaths(),
                Candidates = [.. result.Candidates.Select(candidate => candidate.WithWorkspaceRelativePaths())],
                Error = result.Error.WithWorkspaceRelativePaths()
            };
    }

    extension(ResolvedSymbol? symbol)
    {
        private ResolvedSymbol? WithWorkspaceRelativePaths()
            => symbol is null ? null : symbol with { Location = symbol.Location.WithWorkspaceRelativePaths() };
    }

    extension(Candidate candidate)
    {
        private Candidate WithWorkspaceRelativePaths()
            => candidate with { Location = candidate.Location.WithWorkspaceRelativePaths() };
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
}

internal sealed class QualifiedSymbolQuery
{
    private QualifiedSymbolQuery(string normalizedText, string lookupName, IReadOnlyList<QualifiedNameSegment> containerSegments, QualifiedNameSegment finalSegment, IReadOnlyList<string> parameterTypes, bool hasExplicitParameterList)
    {
        NormalizedText = normalizedText;
        LookupName = lookupName;
        ContainerSegments = containerSegments;
        FinalSegment = finalSegment;
        ParameterTypes = parameterTypes;
        HasExplicitParameterList = hasExplicitParameterList;
    }

    public string NormalizedText { get; }
    public string LookupName { get; }
    public IReadOnlyList<QualifiedNameSegment> ContainerSegments { get; }
    public QualifiedNameSegment FinalSegment { get; }
    public IReadOnlyList<string> ParameterTypes { get; }
    public bool HasExplicitParameterList { get; }
    public bool IsShortNameOnly => ContainerSegments.Count == 0 && !HasExplicitParameterList && FinalSegment.GenericArity is null;

    public static QualifiedSymbolQuery Parse(string normalizedQualifiedName)
    {
        var segments = SplitSegments(normalizedQualifiedName);
        if (segments.Count == 0)
            return new QualifiedSymbolQuery(normalizedQualifiedName, normalizedQualifiedName, [], new QualifiedNameSegment(normalizedQualifiedName, null), [], false);

        var lastSegment = segments[^1];
        var hasExplicitParameterList = TryExtractParameterList(lastSegment, out var memberName, out var parameterListText);
        var finalSegment = ParseSegment(hasExplicitParameterList ? memberName : lastSegment);
        var containerSegments = segments.Take(segments.Count - 1).Select(ParseSegment).ToArray();

        return new QualifiedSymbolQuery(normalizedQualifiedName, finalSegment.Name, containerSegments, finalSegment, hasExplicitParameterList ? SplitParameterList(parameterListText).ToArray() : [], hasExplicitParameterList);
    }

    public bool Matches(ISymbol symbol)
        => symbol.MatchesQualifiedName(NormalizedText) || MatchesType(symbol) || MatchesMember(symbol);

    private bool MatchesType(ISymbol symbol)
    {
        if (HasExplicitParameterList || symbol is not INamedTypeSymbol namedType)
            return false;

        if (!FinalSegment.Matches(namedType.Name, namedType.Arity))
            return false;

        return ContainerSegments.Count == 0 || ContainerSegmentsMatch(namedType.GetQualifiedTypeSegments(includeSelf: false));
    }

    private bool MatchesMember(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol or INamespaceSymbol)
            return false;

        if (!MatchesMemberName(symbol))
            return false;

        if (ContainerSegments.Count > 0 && !ContainerSegmentsMatch(symbol.GetQualifiedContainerSegments()))
            return false;

        return !HasExplicitParameterList || (symbol is IMethodSymbol method && ParametersMatch(method.Parameters));
    }

    private bool MatchesMemberName(ISymbol symbol)
        => symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } method
            ? string.Equals(method.ContainingType?.Name, FinalSegment.Name, StringComparison.Ordinal)
            : string.Equals(symbol.Name, FinalSegment.Name, StringComparison.Ordinal);

    private bool ParametersMatch(ImmutableArray<IParameterSymbol> parameters)
    {
        if (parameters.Length != ParameterTypes.Count)
            return false;

        for (var index = 0; index < parameters.Length; index++)
        {
            if (!ParameterTypeMatches(ParameterTypes[index], parameters[index].Type))
                return false;
        }

        return true;
    }

    private static bool ParameterTypeMatches(string requestedType, ITypeSymbol parameterType)
    {
        var normalizedRequestedType = requestedType.NormalizeQualifiedName().RemoveAllWhitespace();
        return parameterType.GetComparableTypeNames().Any(candidate => string.Equals(candidate, normalizedRequestedType, StringComparison.Ordinal));
    }

    private bool ContainerSegmentsMatch(IReadOnlyList<QualifiedNameSegment> actualSegments)
    {
        if (actualSegments.Count != ContainerSegments.Count)
            return false;

        for (var index = 0; index < actualSegments.Count; index++)
        {
            if (!ContainerSegments[index].Matches(actualSegments[index].Name, actualSegments[index].GenericArity))
                return false;
        }

        return true;
    }

    private static List<string> SplitSegments(string value)
    {
        var segments = new List<string>();
        var start = 0;
        var angleDepth = 0;
        var parenDepth = 0;

        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<': angleDepth++; break;
                case '>': angleDepth = Math.Max(0, angleDepth - 1); break;
                case '(': parenDepth++; break;
                case ')': parenDepth = Math.Max(0, parenDepth - 1); break;
                case '.' when angleDepth == 0 && parenDepth == 0:
                    segments.Add(value[start..index]);
                    start = index + 1;
                    break;
            }
        }

        segments.Add(value[start..]);
        return [.. segments.Select(static segment => segment.Trim()).Where(static segment => segment.Length > 0)];
    }

    private static QualifiedNameSegment ParseSegment(string segment)
    {
        var trimmed = segment.Trim();
        var genericStart = trimmed.IndexOf('<');
        if (genericStart < 0 || !trimmed.EndsWith('>'))
            return new QualifiedNameSegment(trimmed, null);

        var name = trimmed[..genericStart].Trim();
        var genericArguments = trimmed[(genericStart + 1)..^1];
        return new QualifiedNameSegment(name, CountTopLevelItems(genericArguments));
    }

    private static bool TryExtractParameterList(string value, out string memberName, out string parameterListText)
    {
        var openParen = value.IndexOf('(');
        if (openParen < 0 || !value.EndsWith(')'))
        {
            memberName = value;
            parameterListText = string.Empty;
            return false;
        }

        memberName = value[..openParen].Trim();
        parameterListText = value[(openParen + 1)..^1].Trim();
        return true;
    }

    private static IEnumerable<string> SplitParameterList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var start = 0;
        var angleDepth = 0;
        var parenDepth = 0;
        var bracketDepth = 0;

        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<': angleDepth++; break;
                case '>': angleDepth = Math.Max(0, angleDepth - 1); break;
                case '(': parenDepth++; break;
                case ')': parenDepth = Math.Max(0, parenDepth - 1); break;
                case '[': bracketDepth++; break;
                case ']': bracketDepth = Math.Max(0, bracketDepth - 1); break;
                case ',' when angleDepth == 0 && parenDepth == 0 && bracketDepth == 0:
                    yield return value[start..index].Trim();
                    start = index + 1;
                    break;
            }
        }

        yield return value[start..].Trim();
    }

    private static int CountTopLevelItems(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var count = 1;
        var angleDepth = 0;
        var parenDepth = 0;
        var bracketDepth = 0;

        foreach (var character in value)
        {
            switch (character)
            {
                case '<': angleDepth++; break;
                case '>': angleDepth = Math.Max(0, angleDepth - 1); break;
                case '(': parenDepth++; break;
                case ')': parenDepth = Math.Max(0, parenDepth - 1); break;
                case '[': bracketDepth++; break;
                case ']': bracketDepth = Math.Max(0, bracketDepth - 1); break;
                case ',' when angleDepth == 0 && parenDepth == 0 && bracketDepth == 0:
                    count++;
                    break;
            }
        }

        return count;
    }
}
