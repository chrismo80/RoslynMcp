using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Infrastructure;

namespace RoslynMcp.Tools.Inspection.UnderstandProjects.Builders;

internal static class ProjectSummaryBuilder
{
	public static async Task<IReadOnlyList<ProjectSummary>> BuildAsync(Solution solution, bool includeTypes, CancellationToken cancellationToken)
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
			var types = includeTypes
				? await BuildProjectTypesAsync(project, cancellationToken).ConfigureAwait(false)
				: [];

			summaries.Add(new ProjectSummary(
				project.Name,
				project.FilePath,
				outgoingByPath[projectPath].OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
				incomingByPath[projectPath].OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
				types));
		}

		return summaries
			.OrderByDescending(static project => project.OutgoingDependencyProjectPaths.Count + project.IncomingDependencyProjectPaths.Count)
			.ThenBy(static project => project.Name, StringComparer.Ordinal)
			.ToArray();
	}

	private static async Task<IReadOnlyList<string>> BuildProjectTypesAsync(Project project, CancellationToken cancellationToken)
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

			var compactType = $"{type.ToStableId()}: {type.ToQualifiedDisplayName()}";
			var (filePath, _, _) = type.GetDeclarationPosition();

			if (SourceVisibility.ShouldIncludeInHumanResults(filePath))
			{
				visibleTypes.Add(compactType);
				continue;
			}

			generatedFallbackTypes.Add(compactType);
		}

		var selected = visibleTypes.Count > 0 ? visibleTypes : generatedFallbackTypes;
		return selected.OrderBy(static type => type, StringComparer.Ordinal).ToArray();
	}
}