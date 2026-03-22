using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Inspection.RunTests;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRunTestsTool() => services
            .AddSingleton<Service>()
            .AddSingleton<Tool>();
    }

    extension(string? target)
    {
        public Request ToRequest(string? filter) => new(target.NormalizeOptional(), filter.NormalizeOptional());
    }

    internal static Result WithWorkspaceRelativePaths(this Result result)
        => result with
        {
            FailureGroups = [.. result.FailureGroups.Select(static group => group.WithWorkspaceRelativePaths())],
            BuildDiagnostics = result.BuildDiagnostics?.Select(static diagnostic => diagnostic.WithWorkspaceRelativePaths()).ToArray(),
            Error = result.Error.WithWorkspaceRelativePaths()
        };

    private static TestFailureGroup WithWorkspaceRelativePaths(this TestFailureGroup group)
        => group with { File = group.File?.ToWorkspaceRelativePathIfPossible() };

    private static BuildDiagnostic WithWorkspaceRelativePaths(this BuildDiagnostic diagnostic)
        => diagnostic with { File = diagnostic.File?.ToWorkspaceRelativePathIfPossible() };

    private static ErrorInfo? WithWorkspaceRelativePaths(this ErrorInfo? error)
    {
        if (error?.Details is null || error.Details.Count == 0)
            return error;

        Dictionary<string, string>? updated = null;
        foreach (var pair in error.Details)
        {
            if (pair.Key is not ("path" or "file" or "filepath" or "projectpath" or "solutionpath" or "target" or "targetpath" or "provided"))
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
