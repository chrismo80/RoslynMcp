using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadSolution;

public sealed record Result(
    string? SolutionPath,
    IReadOnlyList<ProjectSummary> Projects,
    DiagnosticsSummary? BaselineDiagnostics,
    ErrorInfo? Error = null);

public sealed record ProjectSummary(
    string Name,
    string? Path);

public sealed record DiagnosticsSummary(
    int ErrorCount,
    int WarningCount,
    int InfoCount);

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager)
    : Tool
{
    [McpServerTool(Name = "load_solution", Title = "Load Solution", ReadOnly = false, Idempotent = false)]
    [Description("Use this tool when you need to start working with a .NET solution and no solution has been loaded yet. This must be the first tool called in a session before any code analysis or navigation tools can be used. The result now includes a readiness state so fresh or detached worktrees can be reported as degraded_missing_artifacts or degraded_restore_recommended instead of leaving users to infer that from diagnostics alone.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("(optional): Absolute path to a `.sln` file, or to a directory used as the recursive discovery root for `.sln`/`.slnx` files. If omitted, the tool will auto-detect from the current workspace.")]
        string? solutionHintPath = null
    )
    {
        var solutionPath = solutionHintPath ?? workspaceManager.DiscoverSolutionPaths().FirstOrDefault();

        if (solutionPath is null)
            return new Result(null, [], null, new ErrorInfo("no solution found"));

        var solution = await solutionManager.Load(workspaceManager.ToAbsolutePath(solutionPath), cancellationToken);

        var projects = solution.Projects.ToList();

        var diagnostics = await projects.ToAsyncEnumerable()
            .SelectMany(p => p.Diagnose(cancellationToken))
            .ToArrayAsync(cancellationToken);

        return new Result(
            workspaceManager.ToRelativePathIfPossible(solutionPath),
            projects.ConvertAll(p => p.ToSummary(workspaceManager)),
            diagnostics.ToDiagnosticsSummary());
    }
}

file static class Extensions
{
    extension(Project project)
    {
        public ProjectSummary ToSummary(WorkspaceManager workspaceManager) =>
            new(project.Name, workspaceManager.ToRelativePathIfPossible(project.FilePath));

        public async IAsyncEnumerable<Diagnostic> Diagnose(CancellationToken cancellationToken)
        {
            var compilation = await project
                .GetCompilationAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
                yield return diagnostic;
        }
    }

    extension(IReadOnlyList<Diagnostic> diagnostics)
    {
        public DiagnosticsSummary ToDiagnosticsSummary()
        {
            return new DiagnosticsSummary(
                diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
                diagnostics.Count(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Info or DiagnosticSeverity.Hidden)
            );
        }
    }
}