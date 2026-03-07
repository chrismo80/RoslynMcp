using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Agent;

internal enum SourceKind
{
    HandWritten,
    Generated,
    Intermediate,
    Unknown
}

internal static class SourceVisibility
{
    private static readonly string[] GeneratedFileSuffixes =
    [
        ".g.cs",
        ".g.i.cs",
        ".generated.cs",
        ".designer.cs",
        ".AssemblyAttributes.cs",
        ".AssemblyInfo.cs"
    ];

    public static bool ShouldIncludeInHumanResults(string? path)
        => ClassifyPath(path) is SourceKind.HandWritten or SourceKind.Unknown;

    public static SourceKind ClassifyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return SourceKind.Unknown;
        }

        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(normalized);

        if (normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return SourceKind.Intermediate;
        }

        if (GeneratedFileSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return SourceKind.Generated;
        }

        return SourceKind.HandWritten;
    }

    public static string ResolveProjectName(this ISymbol symbol, Project project)
    {
        var sourceLocation = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
        if (sourceLocation?.SourceTree is not null)
        {
            var document = project.GetDocument(sourceLocation.SourceTree);
            if (document != null)
            {
                return document.Project.Name;
            }
        }

        return project.Name;
    }
}
