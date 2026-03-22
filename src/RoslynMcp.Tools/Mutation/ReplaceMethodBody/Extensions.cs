using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.ReplaceMethodBody;

internal static class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddReplaceMethodBodyTool() => services.AddSingleton<Service>().AddSingleton<Tool>();
	}

	extension(string targetMethodSymbolId)
	{
		public Request ToRequest(string body) => new(targetMethodSymbolId.Trim(), body);
	}

	internal static Result WithWorkspaceRelativePaths(this Result result)
		=> result with
		{
			ChangedFiles = result.ChangedFiles.Select(static path => path.ToWorkspaceRelativePathIfPossible()).ToArray(),
			DiagnosticsDelta = new DiagnosticsDeltaInfo(result.DiagnosticsDelta.NewErrors.Select(static item => item with { FilePath = item.FilePath.ToWorkspaceRelativePathIfPossible() }).ToArray(), result.DiagnosticsDelta.NewWarnings.Select(static item => item with { FilePath = item.FilePath.ToWorkspaceRelativePathIfPossible() }).ToArray())
		};
}
