using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.FormatDocument;

internal static class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddFormatDocumentTool() => services.AddSingleton<Service>().AddSingleton<Tool>();
	}

	extension(string path)
	{
		public Request ToRequest() => new(path.Trim());
	}

	internal static Result WithWorkspaceRelativePaths(this Result result)
		=> result with { Path = result.Path.ToWorkspaceRelativePathIfPossible(), Error = result.Error.WithWorkspaceRelativePaths() };

	private static ErrorInfo? WithWorkspaceRelativePaths(this ErrorInfo? error)
	{
		if (error?.Details is null)
			return error;
		var map = new Dictionary<string, string>(error.Details, StringComparer.Ordinal);
		foreach (var key in new[] { "path", "provided" })
		{
			if (map.TryGetValue(key, out var value))
				map[key] = value.ToWorkspaceRelativePathIfPossible();
		}
		return error with { Details = map };
	}
}
