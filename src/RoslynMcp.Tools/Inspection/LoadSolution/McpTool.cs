using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadSolution;

public sealed record Result(
    string? Path,
    IReadOnlyList<ProjectSummary> Projects,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(null, [], new ErrorInfo(message, details));
}

public sealed record ProjectSummary(
    string Name,
    string? ProjectPath,
    IReadOnlyList<string> References,
    IReadOnlyList<string> ReferencedBy);

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager)
    : Tool
{
    [McpServerTool(Name = "load_solution", Title = "Load Solution", ReadOnly = false, Idempotent = false)]
    [Description("Use this tool when you need to start working with a .NET solution and no solution has been loaded yet. This must be the first tool called in a session before any code analysis or navigation tools can be used.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("(optional): Absolute path to a `.sln` file, or to a directory used as the recursive discovery root for `.sln`/`.slnx` files. If omitted, the tool will auto-detect from the current workspace.")]
        string? solutionHintPath = null
    )
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

        var solution = await solutionManager.Load(solutionPath, cancellationToken);

        return new Result(
            workspaceManager.ToRelativePathIfPossible(solutionPath),
            GetProjects(solution));
    }
    
    private IReadOnlyList<ProjectSummary> GetProjects(Solution solution)
    {
        var outgoingByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var incomingByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            var projectPath = project.FilePath ?? string.Empty;

            outgoingByPath.TryAdd(projectPath, []);
            incomingByPath.TryAdd(projectPath, []);
        }

        foreach (var project in solution.Projects)
        {
            var sourcePath = project.FilePath ?? string.Empty;

            foreach (var reference in project.ProjectReferences)
            {
                var dependency = solution.GetProject(reference.ProjectId);

                if (dependency?.FilePath is null)
                    continue;

                outgoingByPath[sourcePath].Add(dependency.FilePath);
                incomingByPath[dependency.FilePath].Add(sourcePath);
            }
        }

        var summaries = new List<ProjectSummary>();

        foreach (var project in solution.Projects)
        {
            var projectPath = project.FilePath ?? string.Empty;

            summaries.Add(new ProjectSummary(
                project.Name,
                workspaceManager.ToRelativePathIfPossible(project?.FilePath ?? string.Empty),
                [.. outgoingByPath[projectPath].Select(workspaceManager.ToRelativePathIfPossible).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)],
                [.. incomingByPath[projectPath].Select(workspaceManager.ToRelativePathIfPossible).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)]));
        }

        return [.. summaries
            .OrderByDescending(static project => project.ReferencedBy.Count)
            .ThenBy(static project => project.Name, StringComparer.Ordinal)];
    }
}
