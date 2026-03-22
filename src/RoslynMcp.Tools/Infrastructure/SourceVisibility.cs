namespace RoslynMcp.Tools.Infrastructure;

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

    public static bool IsGeneratedLike(string? path)
        => ClassifyPath(path) is SourceKind.Generated or SourceKind.Intermediate;

    private static SourceKind ClassifyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return SourceKind.Unknown;

        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(normalized);

        if (normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return SourceKind.Intermediate;
        }

        if (GeneratedFileSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            return SourceKind.Generated;

        return SourceKind.HandWritten;
    }

    private enum SourceKind
    {
        HandWritten,
        Generated,
        Intermediate,
        Unknown
    }
}
