using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Infrastructure;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.UnderstandProjects;

[McpServerToolType]
public sealed class UnderstandProjectsTool(SolutionManager solutionManager, SymbolManager symbolManager) : Tool
{
    [McpServerTool(Name = "understand_projects", Title = "Understand Projects", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need a quick overview of the loaded solution's project landscape. It returns real project relationships with projectPath lists, compact per-project type summaries for standard/deep profiles, and hotspots only for deep analysis.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("Analysis depth. quick omits types and hotspots, standard includes types, deep includes types and 10 hotspots. Defaults to standard.")]
        string? profile = null)
    {
        if (solutionManager.Solution is null)
        {
            return new Result([], new ErrorInfo("No solution is currently loaded.", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nextAction"] = "Call load_solution first to select a solution before understanding projects."
            }));
        }

        var projects = await BuildAsync(solutionManager.Solution, profile is Extensions.Profiles.Deep, cancellationToken)
            .ConfigureAwait(false);

        return new Result(projects);
    }
    
    private async Task<IReadOnlyList<ProjectSummary>> BuildAsync(Solution solution, bool includeTypes, CancellationToken cancellationToken)
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
            var types = includeTypes ? await BuildProjectTypesAsync(project, cancellationToken).ConfigureAwait(false) : [];

            summaries.Add(new ProjectSummary(
                project.Name,
                project.FilePath,
                [.. outgoingByPath[projectPath].OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)],
                [.. incomingByPath[projectPath].OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)],
                types));
        }

        return [.. summaries
            .OrderByDescending(static project => project.OutgoingDependencyProjectPaths.Count + project.IncomingDependencyProjectPaths.Count)
            .ThenBy(static project => project.Name, StringComparer.Ordinal)];
    }

    private async Task<IReadOnlyList<string>> BuildProjectTypesAsync(Project project, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        if (compilation is null)
            return [];

        var visibleTypes = new List<string>();
        var generatedFallbackTypes = new List<string>();

        foreach (var type in compilation.Assembly.GlobalNamespace.EnumerateTypes())
        {
            if (!type.Locations.Any(static location => location.IsInSource))
                continue;

            var compactType = $"{symbolManager.ToOuterSymbolId(type)}: {type.ToQualifiedDisplayName()}";
            var (filePath, _, _) = type.GetDeclarationPosition();

            if (SourceVisibility.ShouldIncludeInHumanResults(filePath))
            {
                visibleTypes.Add(compactType);
                continue;
            }

            generatedFallbackTypes.Add(compactType);
        }

        var selected = visibleTypes.Count > 0 ? visibleTypes : generatedFallbackTypes;

        return [.. selected.OrderBy(static type => type, StringComparer.Ordinal)];
    }
}
