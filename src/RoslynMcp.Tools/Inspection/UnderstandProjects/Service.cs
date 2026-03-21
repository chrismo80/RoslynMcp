using RoslynMcp.Tools.Inspection.UnderstandProjects.Builders;

namespace RoslynMcp.Tools.Inspection.UnderstandProjects;

public sealed class Service(Infrastructure.Services.Workspace workspace)
{
	private const int DeepHotspotCount = 10;

	public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
		if (session is null)
		{
			return new Result(request.Profile ?? Extensions.Profiles.Standard, [], [],
				new ErrorInfo(
					"no_solution_loaded",
					"No solution is currently loaded.",
					new Dictionary<string, string>(StringComparer.Ordinal)
					{
						["nextAction"] = "Call load_solution first to select a solution before understanding projects."
					})).WithWorkspaceRelativePaths();
		}

		var includeTypes = request.Profile is Extensions.Profiles.Standard or Extensions.Profiles.Deep;
		var projects = await ProjectSummaryBuilder.BuildAsync(session.Solution, includeTypes, cancellationToken).ConfigureAwait(false);

		if (request.Profile != Extensions.Profiles.Deep)
			return new Result(request.Profile!, projects, [])
				.WithWorkspaceRelativePaths();

		var hotspots = await HotspotBuilder.BuildAsync(session.Solution, DeepHotspotCount, cancellationToken).ConfigureAwait(false);

		return new Result(request.Profile!, projects, hotspots)
			.WithWorkspaceRelativePaths();
	}
}