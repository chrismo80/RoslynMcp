using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools;

internal static class Extensions
{
    internal static string WorkspaceRoot => Path.GetFullPath(Directory.GetCurrentDirectory());

    extension(string? input)
    {
        internal string? NormalizeOptional() =>
            string.IsNullOrWhiteSpace(input) ? null : input.Trim();
    }

    extension(string input)
    {
        internal string NormalizeEscapedTypeSyntax() => input
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal);

        internal string NormalizeEscapedNewlines() => input
            .Replace("\\r\\n", "\r\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal);
    }

    extension(string? path)
    {
        internal string ToWorkspaceAbsolutePath(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path!;

            var trimmedPath = path.Trim();
            try
            {
                return Path.IsPathRooted(trimmedPath)
                    ? Path.GetFullPath(trimmedPath)
                    : Path.GetFullPath(trimmedPath, workspaceRoot);
            }
            catch
            {
                return trimmedPath;
            }
        }

        internal bool MatchesByNormalizedPath(string otherPath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(otherPath))
                return false;

            try
            {
                var normalizedPath = Path.GetFullPath(path);
                var normalizedOtherPath = Path.GetFullPath(otherPath);
                return string.Equals(normalizedPath, normalizedOtherPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(path, otherPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        public string ToWorkspaceAbsolutePath()
        {
            if (string.IsNullOrWhiteSpace(path))
                return path!;

            var trimmedPath = path.Trim();
            try
            {
                return Path.IsPathRooted(trimmedPath)
                    ? Path.GetFullPath(trimmedPath)
                    : Path.GetFullPath(trimmedPath, WorkspaceRoot);
            }
            catch
            {
                return trimmedPath;
            }
        }

        public string ToWorkspaceRelativePathIfPossible()
        {
            if (string.IsNullOrWhiteSpace(path))
                return path!;

            var absolutePath = path.ToWorkspaceAbsolutePath();
            if (!Path.IsPathRooted(absolutePath))
                return absolutePath;

            try
            {
                var normalizedWorkspaceRoot = WorkspaceRoot.EnsureTrailingDirectorySeparator();
                var normalizedAbsolutePath = Path.GetFullPath(absolutePath);
                if (!normalizedAbsolutePath.StartsWith(normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
                    return normalizedAbsolutePath;

                return Path.GetRelativePath(WorkspaceRoot, normalizedAbsolutePath);
            }
            catch
            {
                return absolutePath;
            }
        }

        public string ToWorkspaceRelativePathIfPossible(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path!;

            var absolutePath = path.ToWorkspaceAbsolutePath(workspaceRoot);
            if (!Path.IsPathRooted(absolutePath))
                return absolutePath;

            try
            {
                var normalizedWorkspaceRoot = workspaceRoot.EnsureTrailingDirectorySeparator();
                var normalizedAbsolutePath = Path.GetFullPath(absolutePath);
                if (!normalizedAbsolutePath.StartsWith(normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
                    return normalizedAbsolutePath;

                return Path.GetRelativePath(workspaceRoot, normalizedAbsolutePath);
            }
            catch
            {
                return absolutePath;
            }
        }
    }

    extension(ISymbol symbol)
    {
        internal (string FilePath, int? Line, int? Column) GetDeclarationPosition()
        {
            var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);

            if (location is null)
                return (string.Empty, null, null);

            var span = location.GetLineSpan();
            var start = span.StartLinePosition;

            return (span.Path ?? string.Empty, start.Line + 1, start.Character + 1);
        }

        internal string ToStableId() =>
            $"{symbol.Kind}:{(symbol.OriginalDefinition ?? symbol).ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}";
    }

    internal static string? NormalizeNamespace(this INamespaceSymbol? symbol)
        => symbol?.IsGlobalNamespace != false
            ? null
            : symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);

    private static string EnsureTrailingDirectorySeparator(this string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
