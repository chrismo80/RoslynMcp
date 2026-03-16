using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Navigation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcp.Infrastructure.Agent;

public static partial class CodeUnderstandingExtensions
{
    private const int DefaultPageSize = 100;
    private const int MaximumPageSize = 500;

    public static async Task<ResolveSymbolCandidate[]> ResolveByQualifiedNameAsync(
        this string qualifiedName,
        IReadOnlyList<Project> projects,
        CancellationToken ct)
    {
        var normalizedQualifiedName = qualifiedName.NormalizeQualifiedName();
        var query = QualifiedSymbolQuery.Parse(normalizedQualifiedName);
        if (string.IsNullOrWhiteSpace(query.LookupName))
            return [];

        var candidates = new List<(ISymbol Symbol, string ProjectName)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in projects.OrderBy(static p => p.Name, StringComparer.Ordinal))
        {
            var symbols = await SymbolFinder.FindDeclarationsAsync(
                    project,
                    query.LookupName,
                    ignoreCase: false,
                    filter: SymbolFilter.TypeAndMember,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            foreach (var symbol in symbols)
            {
                var normalizedSymbol = symbol.OriginalDefinition ?? symbol;
                var symbolId = normalizedSymbol.CreateId();

                if (!seen.Add(symbolId))
                    continue;

                candidates.Add((normalizedSymbol, normalizedSymbol.ResolveProjectName(project)));
            }
        }

        var strictMatches = candidates
            .Where(match => query.Matches(match.Symbol))
            .ToArray();

        if (strictMatches.Length > 0)
            return OrderResolveSymbolCandidates(strictMatches, query.LookupName);

        if (!query.IsShortNameOnly)
            return [];

        var caseSensitiveMatches = candidates
            .Where(match => string.Equals(match.Symbol.Name, query.LookupName, StringComparison.Ordinal))
            .ToArray();

        var shortNameMatches = caseSensitiveMatches.Length > 0
            ? caseSensitiveMatches
            : candidates
                .Where(match => string.Equals(match.Symbol.Name, query.LookupName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return OrderResolveSymbolCandidates(shortNameMatches, query.LookupName);
    }

    public static IReadOnlyList<Project> ResolveProjectSelector(
        this Solution solution,
        string? projectPath,
        string? projectName,
        string? projectId,
        bool selectorRequired,
        string toolName,
        out ErrorInfo? error)
    {
        var normalizedPath = projectPath.NormalizeOptional();
        var normalizedName = projectName.NormalizeOptional();
        var normalizedId = projectId.NormalizeOptional();

        if (normalizedPath == null && normalizedName == null && normalizedId == null)
        {
            if (selectorRequired)
            {
                error = AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "A project selector is required. Provide projectPath, projectName, or projectId.",
                    $"Call {toolName} with one project selector from load_solution results.",
                    ("field", "project selector"),
                    ("expected", "projectPath|projectName|projectId"));
                return Array.Empty<Project>();
            }

            error = null;
            return solution.Projects.OrderBy(static p => p.Name, StringComparer.Ordinal).ToArray();
        }

        var matches = solution.Projects
            .Where(project => normalizedPath == null || project.FilePath.MatchesByNormalizedPath(normalizedPath))
            .Where(project => normalizedName == null || string.Equals(project.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
            .Where(project => normalizedId == null || string.Equals(project.Id.Id.ToString(), normalizedId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
        {
            var provided = string.Join(", ",
                new[]
                {
                    normalizedPath is null ? null : $"projectPath={normalizedPath}",
                    normalizedName is null ? null : $"projectName={normalizedName}",
                    normalizedId is null ? null : $"projectId={normalizedId}"
                }.Where(static value => value != null)!);

            var message = normalizedId == null
                ? "Project selector did not match any loaded project."
                : "projectId did not match any project in the active workspace snapshot.";
            var nextAction = normalizedId == null
                ? "Use load_solution output to provide an exact projectPath, projectName, or projectId."
                : "projectId values are snapshot-local and can change after reload. Refresh selectors from the current snapshot or prefer projectPath for automation.";

            error = AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                message,
                nextAction,
                ("field", "project selector"),
                ("provided", provided),
                ("projectIdScope", normalizedId == null ? null : "snapshot-local"));
            return [];
        }

        if (matches.Length > 1)
        {
            var names = string.Join(", ", matches.Select(static project => project.Name));
            error = AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "Project selector is ambiguous and matched multiple projects.",
                "Provide projectPath or projectId to uniquely identify the project.",
                ("field", "project selector"),
                ("matches", names));
            return [];
        }

        error = null;
        return matches;
    }

    public static string NormalizeProfile(this string? profile)
    {
        var normalized = string.IsNullOrWhiteSpace(profile) ? "standard" : profile.Trim().ToLowerInvariant();
        return normalized is "quick" or "standard" or "deep" ? normalized : "standard";
    }

    public static (int Offset, int Limit) NormalizePaging(this int? offset, int? limit)
    {
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var normalizedLimit = limit.HasValue
            ? Math.Clamp(limit.Value, 0, MaximumPageSize)
            : DefaultPageSize;
        return (normalizedOffset, normalizedLimit);
    }

    public static bool TryNormalizeAccessibility(this string? accessibility, out string? normalized)
    {
        var value = accessibility.NormalizeOptional();
        if (value == null)
        {
            normalized = null;
            return true;
        }

        normalized = value.Replace('-', '_').ToLowerInvariant();
        if (normalized is "public" or "internal" or "protected" or "private" or "protected_internal" or "private_protected")
        {
            return true;
        }

        normalized = null;
        return false;
    }

    public static bool TryNormalizeTypeKind(this string? kind, out string? normalized)
    {
        normalized = kind.NormalizeOptional()?.ToLowerInvariant();
        if (normalized == null)
        {
            return true;
        }

        if (normalized is "class" or "record" or "interface" or "enum" or "struct")
        {
            return true;
        }

        normalized = null;
        return false;
    }

    public static bool TryNormalizeMemberKind(this string? kind, out string? normalized)
    {
        normalized = kind.NormalizeOptional()?.ToLowerInvariant();
        if (normalized == null)
        {
            return true;
        }

        if (normalized is "method" or "property" or "field" or "event" or "ctor")
        {
            return true;
        }

        normalized = null;
        return false;
    }

    public static bool TryNormalizeBinding(this string? binding, out string? normalized)
    {
        normalized = binding.NormalizeOptional()?.ToLowerInvariant();
        if (normalized == null)
        {
            return true;
        }

        if (normalized is "static" or "instance")
        {
            return true;
        }

        normalized = null;
        return false;
    }

    public static bool TryNormalizeDependencyDirection(this string? direction, out string normalized)
    {
        normalized = direction.NormalizeOptional()?.ToLowerInvariant() ?? "both";
        return normalized is "outgoing" or "incoming" or "both";
    }

    public static string? NormalizeOptional(this string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string ResolveDiscoveryRoot(this string? workspaceHintPath)
    {
        var normalizedHint = workspaceHintPath.NormalizeOptional();
        if (normalizedHint == null)
        {
            return Directory.GetCurrentDirectory();
        }

        if (normalizedHint.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || normalizedHint.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(normalizedHint);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Directory.GetCurrentDirectory();
            }

            if (normalizedHint.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(directory);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    return parent;
                }
            }

            return directory;
        }

        return normalizedHint;
    }

    private static ResolveSymbolCandidate[] OrderResolveSymbolCandidates(
        IReadOnlyList<(ISymbol Symbol, string ProjectName)> matches,
        string shortName)
    {
        return matches
            .OrderByDescending(match => string.Equals(match.Symbol.Name, shortName, StringComparison.Ordinal))
            .ThenBy(match => GetResolveSymbolKindPriority(match.Symbol))
            .ThenBy(match => match.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), StringComparer.Ordinal)
            .ThenBy(match => match.ProjectName, StringComparer.Ordinal)
            .ThenBy(match => match.Symbol.CreateId(), StringComparer.Ordinal)
            .Select(match =>
            {
                var (filePath, line, column) = match.Symbol.GetDeclarationPosition();
                return new ResolveSymbolCandidate(
                    match.Symbol.CreateId().ToExternal(),
                    match.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    match.Symbol.Kind.ToString(),
                    CreateOptionalSourceLocation(filePath, line, column),
                    match.ProjectName,
                    match.Symbol.ToQualifiedDisplayName());
            })
            .ToArray();
    }

    private static int GetResolveSymbolKindPriority(ISymbol symbol)
        => symbol is INamedTypeSymbol ? 0 : 1;
}
