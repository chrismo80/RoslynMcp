using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Inspection.FindCodeSmells;

internal static class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddFindCodeSmellsTool() => services
			.AddSingleton<Service>()
			.AddSingleton<Tool>();
	}

	extension(string path)
	{
		public Request ToRequest(int? maxFindings, IReadOnlyList<string>? riskLevels, IReadOnlyList<string>? categories, string? reviewMode)
			=> new(path.Trim(), maxFindings, riskLevels?.Select(static value => value?.Trim() ?? string.Empty).ToArray(), categories?.Select(static value => value?.Trim() ?? string.Empty).ToArray(), reviewMode.NormalizeOptional()?.ToLowerInvariant());
	}

	internal static Result WithWorkspaceRelativePaths(this Result result)
		=> result with
		{
			Findings = result.Findings.Select(static finding => finding.WithWorkspaceRelativePaths()).ToArray(),
			Error = result.Error.WithWorkspaceRelativePaths()
		};

	private static CodeSmellFindingEntry WithWorkspaceRelativePaths(this CodeSmellFindingEntry finding)
		=> finding with
		{
			OccurrenceFiles = finding.OccurrenceFiles.Select(static file => file with { FilePath = file.FilePath.ToWorkspaceRelativePathIfPossible() }).ToArray()
		};

	private static ErrorInfo? WithWorkspaceRelativePaths(this ErrorInfo? error)
	{
		if (error?.Details is null || error.Details.Count == 0)
			return error;

		Dictionary<string, string>? updated = null;
		foreach (var pair in error.Details)
		{
			if (pair.Key is not ("path" or "file" or "filepath" or "provided"))
				continue;

			var outward = pair.Value.ToWorkspaceRelativePathIfPossible();
			if (string.Equals(outward, pair.Value, StringComparison.Ordinal))
				continue;

			updated ??= new Dictionary<string, string>(error.Details, StringComparer.Ordinal);
			updated[pair.Key] = outward;
		}

		return updated is null ? error : error with { Details = updated };
	}
}
