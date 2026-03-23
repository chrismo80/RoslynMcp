namespace RoslynMcp.Tools.Managers;

public sealed class WorkspaceManager
{
    public string WorkspaceDirectory { get; private set; } = Directory.GetCurrentDirectory();
    
    public void SetWorkspaceDirectory(string dir)
    {
        if (Path.IsPathRooted(dir) && Directory.Exists(dir))
            WorkspaceDirectory = dir;
    }

    public string ToAbsolutePath(string relativePath) =>
        Path.Combine(WorkspaceDirectory, relativePath);

    public string ToRelativePath(string path) =>
        Path.GetRelativePath(WorkspaceDirectory, path);

    internal IReadOnlyList<string> DiscoverSolutionPaths() =>
        DiscoverSolutionPaths("*.sln", "*.slnx").Order().ToList();
    
    private IEnumerable<string> DiscoverSolutionPaths(params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            foreach (var solutionPath in Directory.EnumerateFiles(WorkspaceDirectory, pattern, SearchOption.AllDirectories))
            {
                yield return Path.GetFullPath(solutionPath);
            }
        }
    }
}
