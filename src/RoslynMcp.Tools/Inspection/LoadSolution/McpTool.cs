using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadSolution;

public sealed record Result(
    string? Path,
    ProjectBuckets? Projects,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(null, null, new ErrorInfo(message, details));
}

public sealed record ProjectBuckets(
    IReadOnlyList<ProjectEntry>? Roots,
    IReadOnlyList<ProjectEntry>? Leaves,
    IReadOnlyList<ProjectEntry>? Interior,
    IReadOnlyList<ProjectEntry>? Isolated);

public sealed record ProjectEntry(
    string Name,
    string ProjectPath,
    IReadOnlyList<string>? References = null);

[McpServerToolType]
public sealed class McpTool(WorkspaceManager workspaceManager, SolutionManager solutionManager)
{
    [McpServerTool(Name = "load_solution", Title = "Load Solution", ReadOnly = false, Idempotent = false)]
    [Description("Use this tool when you need to start working with a .NET solution and no solution has been loaded yet. This must be the first tool called in a session before any code analysis or navigation tools can be used. Opening large or complex solutions can take a while, so use a generous tool/request timeout if your client supports it.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("(optional): Absolute path to a `.sln` file, or to a directory used as the recursive discovery root for `.sln`/`.slnx` files. If omitted, the tool will auto-detect from the current workspace.")]
        string? solutionHintPath = null)
    {
        solutionHintPath = workspaceManager.ToAbsolutePath(solutionHintPath);

        var solutionPath = solutionHintPath switch
        {
            null => workspaceManager.DiscoverSolutionPaths().FirstOrDefault(),
            _ when File.Exists(solutionHintPath) => solutionHintPath,
            _ when Directory.Exists(solutionHintPath) => solutionHintPath.DiscoverFiles("*.sln", "*.slnx").OrderBy(path => path.Length).FirstOrDefault(),
            _ => null
        };

        if (solutionPath is null)
            return Result.AsError("no solution found");

        var solution = await solutionManager.Load(solutionPath, cancellationToken).ConfigureAwait(false);

        return new Result(
            workspaceManager.ToRelativePathIfPossible(solutionPath),
            BuildProjectBuckets(solution));
    }

    private ProjectBuckets? BuildProjectBuckets(Solution solution)
    {
        var projects = GetProjects(solution);

        var roots = OrderRoots(projects.Where(IsRoot));
        var leaves = OrderLeaves(projects.Where(IsLeaf));
        var interior = OrderInterior(projects.Where(IsInterior).ToList());
        var isolated = OrderIsolated(projects.Where(IsIsolated));

        var buckets = new ProjectBuckets(
            NullIfEmpty(roots),
            NullIfEmpty(leaves),
            NullIfEmpty(interior),
            NullIfEmpty(isolated));

        return buckets.Roots is null && buckets.Leaves is null && buckets.Interior is null && buckets.Isolated is null
            ? null
            : buckets;
    }

    private List<GraphProject> GetProjects(Solution solution)
    {
        var emittedProjects = solution.Projects
            .Select(project => (Project: project, ProjectPath: ToRelativeProjectPath(project.FilePath)))
            .Where(project => !string.IsNullOrWhiteSpace(project.ProjectPath))
            .Select(project => (project.Project, ProjectPath: project.ProjectPath!))
            .DistinctBy(p => p.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        var projectPathsById = emittedProjects.ToDictionary(project => project.Project.Id, project => project.ProjectPath);
        var referencesByPath = emittedProjects.ToDictionary(
            project => project.ProjectPath,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var incomingCounts = emittedProjects.ToDictionary(
            project => project.ProjectPath,
            _ => 0,
            StringComparer.OrdinalIgnoreCase);

        foreach (var (project, projectPath) in emittedProjects)
        {
            foreach (var reference in project.ProjectReferences)
            {
                if (!projectPathsById.TryGetValue(reference.ProjectId, out var referencedProjectPath))
                    continue;

                if (referencesByPath[projectPath].Add(referencedProjectPath))
                    incomingCounts[referencedProjectPath]++;
            }
        }

        return emittedProjects
            .Select(project => new GraphProject(
                project.Project.Name,
                project.ProjectPath,
                referencesByPath[project.ProjectPath]
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                incomingCounts[project.ProjectPath]))
            .ToList();
    }

    private string? ToRelativeProjectPath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return null;

        var relativeProjectPath = workspaceManager.ToRelativePathIfPossible(projectPath);
        return string.IsNullOrWhiteSpace(relativeProjectPath) ? null : relativeProjectPath;
    }

    private static IReadOnlyList<ProjectEntry> OrderIsolated(IEnumerable<GraphProject> projects)
        => projects
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ThenBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .Select(ToEntry)
            .ToList();

    private static IReadOnlyList<ProjectEntry> OrderRoots(IEnumerable<GraphProject> projects)
        => projects
            .OrderByDescending(project => project.OutgoingCount)
            .ThenBy(project => project.Name, StringComparer.Ordinal)
            .ThenBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .Select(ToEntry)
            .ToList();

    private static IReadOnlyList<ProjectEntry> OrderLeaves(IEnumerable<GraphProject> projects)
        => projects
            .OrderByDescending(project => project.IncomingCount)
            .ThenBy(project => project.Name, StringComparer.Ordinal)
            .ThenBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .Select(ToEntry)
            .ToList();

    private static IReadOnlyList<ProjectEntry> OrderInterior(IReadOnlyList<GraphProject> projects)
    {
        if (projects.Count == 0)
            return [];

        var projectsByPath = projects.ToDictionary(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase);
        var remainingProjectPaths = new HashSet<string>(projectsByPath.Keys, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ProjectEntry>(projects.Count);

        while (remainingProjectPaths.Count > 0)
        {
            var remainingCounts = GetRemainingCounts(remainingProjectPaths, projectsByPath);

            var isolated = remainingProjectPaths
                .Where(projectPath => remainingCounts.IncomingCounts[projectPath] == 0 && remainingCounts.OutgoingCounts[projectPath] == 0)
                .Select(projectPath => projectsByPath[projectPath])
                .OrderBy(project => project.Name, StringComparer.Ordinal)
                .ThenBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var roots = remainingProjectPaths
                .Where(projectPath => remainingCounts.IncomingCounts[projectPath] == 0 && remainingCounts.OutgoingCounts[projectPath] > 0)
                .Select(projectPath => projectsByPath[projectPath])
                .OrderByDescending(project => remainingCounts.OutgoingCounts[project.ProjectPath])
                .ThenBy(project => project.Name, StringComparer.Ordinal)
                .ThenBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var leaves = remainingProjectPaths
                .Where(projectPath => remainingCounts.IncomingCounts[projectPath] > 0 && remainingCounts.OutgoingCounts[projectPath] == 0)
                .Select(projectPath => projectsByPath[projectPath])
                .OrderByDescending(project => remainingCounts.IncomingCounts[project.ProjectPath])
                .ThenBy(project => project.Name, StringComparer.Ordinal)
                .ThenBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (isolated.Count == 0 && roots.Count == 0 && leaves.Count == 0)
                break;

            foreach (var project in isolated)
            {
                ordered.Add(ToEntry(project));
                remainingProjectPaths.Remove(project.ProjectPath);
            }

            foreach (var project in Interleave(roots, leaves))
            {
                ordered.Add(ToEntry(project));
                remainingProjectPaths.Remove(project.ProjectPath);
            }
        }

        ordered.AddRange(remainingProjectPaths
            .Select(projectPath => projectsByPath[projectPath])
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ThenBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .Select(ToEntry));

        return ordered;
    }

    private static RemainingCounts GetRemainingCounts(
        HashSet<string> remainingProjectPaths,
        IReadOnlyDictionary<string, GraphProject> projectsByPath)
    {
        var incomingCounts = remainingProjectPaths.ToDictionary(projectPath => projectPath, _ => 0, StringComparer.OrdinalIgnoreCase);
        var outgoingCounts = remainingProjectPaths.ToDictionary(projectPath => projectPath, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var projectPath in remainingProjectPaths)
        {
            foreach (var reference in projectsByPath[projectPath].References)
            {
                if (!remainingProjectPaths.Contains(reference))
                    continue;

                outgoingCounts[projectPath]++;
                incomingCounts[reference]++;
            }
        }

        return new RemainingCounts(incomingCounts, outgoingCounts);
    }

    private static List<GraphProject> Interleave(IReadOnlyList<GraphProject> first, IReadOnlyList<GraphProject> second)
    {
        var interleaved = new List<GraphProject>(first.Count + second.Count);

        for (var index = 0; index < Math.Max(first.Count, second.Count); index++)
        {
            if (index < first.Count)
                interleaved.Add(first[index]);

            if (index < second.Count)
                interleaved.Add(second[index]);
        }

        return interleaved;
    }

    private static ProjectEntry ToEntry(GraphProject project)
        => new(project.Name, project.ProjectPath, NullIfEmpty(project.References));

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> values)
        => values.Count == 0 ? null : values;

    private static bool IsIsolated(GraphProject project) => project.IncomingCount == 0 && project.OutgoingCount == 0;

    private static bool IsRoot(GraphProject project) => project.IncomingCount == 0 && project.OutgoingCount > 0;

    private static bool IsLeaf(GraphProject project) => project.IncomingCount > 0 && project.OutgoingCount == 0;

    private static bool IsInterior(GraphProject project) => project.IncomingCount > 0 && project.OutgoingCount > 0;

    private sealed record GraphProject(
        string Name,
        string ProjectPath,
        IReadOnlyList<string> References,
        int IncomingCount)
    {
        internal int OutgoingCount => References.Count;
    }

    private sealed record RemainingCounts(
        IReadOnlyDictionary<string, int> IncomingCounts,
        IReadOnlyDictionary<string, int> OutgoingCounts);
}
