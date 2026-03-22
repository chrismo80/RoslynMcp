using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Tests;

public sealed class SharedSandboxContext : SandboxContext
{
    internal Task InitializeAsync(CancellationToken cancellationToken = default)
        => InitializeSandboxAsync(TestSolutionSandbox.Create(CanonicalTestSolutionDirectory), cancellationToken);

    public Project GetProject(string projectName)
        => GetCurrentSolution().Projects.Single(project => project.Name == projectName);

    public Solution GetCurrentSolution()
    {
        var session = GetRequiredService<RoslynMcp.Tools.Infrastructure.Services.Workspace>().GetCurrentAsync(CancellationToken.None).GetAwaiter().GetResult();
        return session?.Solution ?? throw new InvalidOperationException("No solution is currently loaded.");
    }
}
