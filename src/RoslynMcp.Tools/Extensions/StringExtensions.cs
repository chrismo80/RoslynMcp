namespace RoslynMcp.Tools.Extensions;

internal static class PathExtensions
{
    extension(string? input)
    {
        internal string? NormalizePathSeparators()
        {
            return input?.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
        
        internal bool IsNullOrEmpty()
        {
            return string.IsNullOrEmpty(input);
        }
    }
}
