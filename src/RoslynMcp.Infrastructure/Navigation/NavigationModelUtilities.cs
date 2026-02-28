using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Navigation;

internal static class NavigationModelUtilities
{
    public static SymbolDescriptor CreateDescriptor(ISymbol symbol)
    {
        var descriptorId = SymbolIdentity.CreateId(symbol);
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        var sourceLocation = location != null ? CreateSourceLocation(location) : new SourceLocation(string.Empty, 1, 1);
        return new SymbolDescriptor(
            descriptorId,
            symbol.Name,
            symbol.Kind.ToString(),
            symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sourceLocation);
    }

    public static SourceLocation CreateSourceLocation(Location location)
    {
        var span = location.GetLineSpan();
        var path = span.Path ?? string.Empty;
        var start = span.StartLinePosition;
        return new SourceLocation(path, start.Line + 1, start.Character + 1);
    }

    public static string GetLocationKey(SourceLocation location)
        => string.Join(':', location.FilePath, location.Line, location.Column);

    public static string GetEdgeKey(CallEdge edge)
        => string.Join(':', edge.FromSymbolId, edge.ToSymbolId, edge.Location.FilePath, edge.Location.Line, edge.Location.Column);

    public static string GetOutlineMemberKey(SymbolMemberOutline member)
        => string.Join('|', member.Name, member.Kind, member.Signature, member.Accessibility, member.IsStatic);

    public static bool MatchesByNormalizedPath(string? candidatePath, string path)
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
}
