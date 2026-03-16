using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using RoslynMcp.Infrastructure.Agent;

namespace RoslynMcp.Infrastructure.Navigation;

internal static class NavigationModelExtensions
{
    public static SymbolDescriptor ToSymbolDescriptor(this ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static l => l.IsInSource);
        var sourceLocation = location != null ? location.ToSourceLocation() : new SourceLocation(string.Empty, 1, 1);
        return new SymbolDescriptor(
            symbol.CreateId().ToExternal(),
            symbol.Name,
            symbol.Kind.ToString(),
            symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sourceLocation);
    }

    public static CompactSymbolSummary ToCompactSymbolSummary(this ISymbol symbol)
    {
        var (filePath, line, column) = symbol.GetDeclarationPosition();
        return new CompactSymbolSummary(
            symbol.CreateId().ToExternal(),
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            symbol.Kind.ToString(),
            CreateOptionalSourceLocation(filePath, line, column),
            symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ?? NormalizeOptional(symbol.ContainingNamespace.NormalizeNamespace()));
    }

    private static SourceLocation? CreateOptionalSourceLocation(string filePath, int? line, int? column)
        => string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue
            ? null
            : new SourceLocation(filePath, line.Value, column.Value);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static SourceLocation ToSourceLocation(this Location location)
    {
        var span = location.GetLineSpan();
        var path = span.Path ?? string.Empty;
        var start = span.StartLinePosition;
        return new SourceLocation(path, start.Line + 1, start.Character + 1);
    }

    public static string GetLocationKey(this SourceLocation location)
        => string.Join(':', location.FilePath, location.Line, location.Column);

    public static string GetEdgeKey(this CallEdge edge)
        => string.Join(':', edge.FromSymbolId, edge.ToSymbolId, edge.Location.FilePath, edge.Location.Line, edge.Location.Column);

    public static string GetOutlineMemberKey(this SymbolMemberOutline member)
        => string.Join('|', member.Name, member.Kind, member.Signature, member.Accessibility, member.IsStatic);

    public static bool MatchesByNormalizedPath(this string? candidatePath, string path)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var normalizedCandidate = Path.GetFullPath(candidatePath);
            var normalizedPath = Path.GetFullPath(path);
            return string.Equals(normalizedCandidate, normalizedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(candidatePath, path, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsValidSearchScope(this string scope)
        => string.Equals(scope, SymbolSearchScopes.Document, StringComparison.Ordinal)
           || string.Equals(scope, SymbolSearchScopes.Project, StringComparison.Ordinal)
           || string.Equals(scope, SymbolSearchScopes.Solution, StringComparison.Ordinal);

    public static bool PathExistsInScope(this Solution solution, string scope, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(scope, SymbolSearchScopes.Solution, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(scope, SymbolSearchScopes.Document, StringComparison.Ordinal))
        {
            return solution.Projects
                .SelectMany(static p => p.Documents)
                .Any(document => document.FilePath.MatchesByNormalizedPath(path));
        }

        return solution.Projects.Any(project =>
            project.FilePath.MatchesByNormalizedPath(path)
            || string.Equals(project.Name, path, StringComparison.OrdinalIgnoreCase));
    }
}
