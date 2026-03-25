using RoslynMcp.Tools.Extensions;

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

    internal IReadOnlyList<string> DiscoverSolutionPaths() => WorkspaceDirectory.DiscoverFiles("*.sln", "*.slnx")
        .OrderBy(path => path.Length)
        .ToList();
}