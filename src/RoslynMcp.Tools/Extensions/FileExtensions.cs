namespace RoslynMcp.Tools.Extensions;

internal static class FileExtensions
{
    extension(string path)
    {
        internal IEnumerable<string> DiscoverFiles(params string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                foreach (var solutionPath in Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
                {
                    yield return Path.GetFullPath(solutionPath);
                }
            }
        }
    }
}