namespace RoslynMcp.Tools.Extensions;

internal static class PathExtensions
{
    extension(string path)
    {
        internal string NormalizePathSeparators()
        {
            return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
    }
}
