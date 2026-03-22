using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.RenameSymbol;

internal static class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddRenameSymbolTool() => services.AddSingleton<Service>().AddSingleton<Tool>();
	}

	extension(string symbolId)
	{
		public Request ToRequest(string newName) => new(symbolId.Trim(), newName.Trim());
	}

	internal static Result WithWorkspaceRelativePaths(this Result result)
		=> result with
		{
			AffectedLocationFiles = result.AffectedLocationFiles.Select(static file => file with { FilePath = file.FilePath.ToWorkspaceRelativePathIfPossible() }).ToArray(),
			ChangedFiles = result.ChangedFiles.Select(static path => path.ToWorkspaceRelativePathIfPossible()).ToArray(),
			Error = result.Error
		};
}
