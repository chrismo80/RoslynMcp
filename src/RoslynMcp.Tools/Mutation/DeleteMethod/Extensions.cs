using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.DeleteMethod;

internal static class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddDeleteMethodTool() => services.AddSingleton<Service>().AddSingleton<Tool>();
	}

	extension(string targetMethodSymbolId)
	{
		public Request ToRequest() => new(targetMethodSymbolId.Trim());
	}

	internal static Result WithWorkspaceRelativePaths(this Result result)
		=> result with
		{
			ChangedFiles = result.ChangedFiles.Select(static path => path.ToWorkspaceRelativePathIfPossible()).ToArray(),
			DiagnosticsDelta = new DiagnosticsDeltaInfo(result.DiagnosticsDelta.NewErrors.Select(static item => item with { FilePath = item.FilePath.ToWorkspaceRelativePathIfPossible() }).ToArray(), result.DiagnosticsDelta.NewWarnings.Select(static item => item with { FilePath = item.FilePath.ToWorkspaceRelativePathIfPossible() }).ToArray())
		};
}
