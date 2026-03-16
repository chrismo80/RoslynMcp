using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

/// <summary>
/// Lists project dependencies in outgoing, incoming, or both directions.
/// Returns project metadata and dependency graph edges.
/// </summary>
internal sealed class ListDependenciesHandler(CodeUnderstandingQueryService queries)
{
    public async Task<ListDependenciesResult> HandleAsync(ListDependenciesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await queries.GetCurrentSolutionAsync(
            "Call load_solution first to list dependencies.",
            ct).ConfigureAwait(false);
        if (solution == null)
            return new ListDependenciesResult([], 0, AgentErrorInfo.Normalize(solutionError, "Call load_solution first to list dependencies."), []);

        if (!request.Direction.TryNormalizeDependencyDirection(out var direction))
        {
            return new ListDependenciesResult([], 0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    $"direction '{request.Direction}' is not valid.",
                    "Use 'outgoing', 'incoming', or 'both'.",
                    ("field", "direction"),
                    ("provided", request.Direction ?? string.Empty)),
                []);
        }

        var hasProjectPath = !string.IsNullOrWhiteSpace(request.ProjectPath);
        var hasProjectName = !string.IsNullOrWhiteSpace(request.ProjectName);
        var hasProjectId = !string.IsNullOrWhiteSpace(request.ProjectId);
        var selectorCount = (hasProjectPath ? 1 : 0) + (hasProjectName ? 1 : 0) + (hasProjectId ? 1 : 0);

        if (selectorCount == 0)
        {
            return new ListDependenciesResult([], 0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "A project selector is required. Provide exactly one of projectPath, projectName, or projectId.",
                    "Call list_dependencies with one selector from load_solution results.",
                    ("field", "project selector"),
                    ("expected", "projectPath|projectName|projectId")),
                []);
        }

        if (selectorCount > 1)
        {
            return new ListDependenciesResult([], 0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "Multiple project selectors provided. Provide exactly one of projectPath, projectName, or projectId.",
                    "Specify only one selector to identify the target project.",
                    ("selectors", $"projectPath:{hasProjectPath}, projectName:{hasProjectName}, projectId:{hasProjectId}")),
                []);
        }

        var normalizedProjectName = request.ProjectName.NormalizeOptional();
        if (normalizedProjectName != null)
        {
            var matchingByName = solution.Projects
                .Where(p => string.Equals(p.Name, normalizedProjectName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matchingByName.Length > 1)
            {
                return new ListDependenciesResult([], 0,
                    AgentErrorInfo.Create(
                        ErrorCodes.AmbiguousSymbol,
                        $"projectName '{request.ProjectName}' matched {matchingByName.Length} projects.",
                        "Use projectPath or projectId to disambiguate.",
                        ("field", "projectName"),
                        ("provided", normalizedProjectName),
                        ("matchingCount", matchingByName.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                    []);
            }
        }

        var selectedProjects = solution.ResolveProjectSelector(
            request.ProjectPath,
            request.ProjectName,
            request.ProjectId,
            selectorRequired: true,
            toolName: "list_dependencies",
            out var selectorError);

        if (selectorError != null)
            return new ListDependenciesResult([], 0, selectorError, []);

        var targetProject = selectedProjects[0];
        var edgeByKey = new Dictionary<string, ProjectDependencyEdge>(StringComparer.Ordinal);
        var dependencyById = new Dictionary<string, ProjectDependency>(StringComparer.Ordinal);

        if (direction == "outgoing" || direction == "both")
        {
            foreach (var reference in targetProject.ProjectReferences.OrderBy(static r => r.ProjectId.Id.ToString(), StringComparer.Ordinal))
            {
                var dependencyProject = solution.GetProject(reference.ProjectId);
                if (dependencyProject == null)
                {
                    continue;
                }

                AddDependencyEdge(targetProject, dependencyProject, edgeByKey, dependencyById, counterpart: dependencyProject);
            }
        }

        if (direction == "incoming" || direction == "both")
        {
            foreach (var project in solution.Projects)
            {
                if (project.ProjectReferences.Any(r => r.ProjectId == targetProject.Id))
                {
                    AddDependencyEdge(project, targetProject, edgeByKey, dependencyById, counterpart: project);
                }
            }
        }

        var orderedEdges = edgeByKey.Values
            .OrderBy(static edge => edge.FromProjectId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.ToProjectId, StringComparer.Ordinal)
            .ToArray();

        var dependencies = dependencyById.Values
            .OrderBy(static dependency => dependency.ProjectName, StringComparer.Ordinal)
            .ThenBy(static dependency => dependency.ProjectId, StringComparer.Ordinal)
            .ToArray();

        return new ListDependenciesResult(dependencies, dependencies.Length, null, orderedEdges);
    }

    private static void AddDependencyEdge(
        Project source,
        Project target,
        IDictionary<string, ProjectDependencyEdge> edgeByKey,
        IDictionary<string, ProjectDependency> dependencyById,
        Project counterpart)
    {
        var sourceDependency = ToProjectDependency(source);
        var targetDependency = ToProjectDependency(target);
        var edgeKey = $"{sourceDependency.ProjectId}->{targetDependency.ProjectId}";
        edgeByKey[edgeKey] = new ProjectDependencyEdge(sourceDependency.ProjectId, targetDependency.ProjectId);

        var counterpartDependency = ToProjectDependency(counterpart);
        dependencyById[counterpartDependency.ProjectId] = counterpartDependency;
    }

    private static ProjectDependency ToProjectDependency(Project project)
        => new(project.Name, project.Id.Id.ToString());
}
