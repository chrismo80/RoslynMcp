namespace RoslynMcp.Tools.Managers;

public sealed class WorkspaceManager : Manager
{
    internal string WorkspaceDirectory { get; private set; } = Directory.GetCurrentDirectory();

    public void SetWorkspaceDirectory(string dir)
    {
        if (Path.IsPathRooted(dir) && Directory.Exists(dir))
            WorkspaceDirectory = dir;
    }

    internal string ToAbsolutePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(WorkspaceDirectory, path);

    internal string ToRelativePathIfPossible(string path) =>
        path.StartsWith(WorkspaceDirectory + Path.DirectorySeparatorChar) ? Path.GetRelativePath(WorkspaceDirectory, path) : path;

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
