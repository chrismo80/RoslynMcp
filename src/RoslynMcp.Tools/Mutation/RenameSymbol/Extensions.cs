using Microsoft.Extensions.DependencyInjection;

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
            AffectedLocationFiles = [.. result.AffectedLocationFiles.Select(static file => file with { FilePath = file.FilePath.ToWorkspaceRelativePathIfPossible() })],
            ChangedFiles = [.. result.ChangedFiles.Select(static path => path.ToWorkspaceRelativePathIfPossible())],
            Error = result.Error
        };
}