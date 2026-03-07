namespace RoslynMcp.Features.Tests;

public sealed class SharedSandboxContext : SandboxContext
{
    internal Task InitializeAsync(CancellationToken cancellationToken = default)
        => InitializeSandboxAsync(TestSolutionSandbox.Create(CanonicalTestSolutionDirectory), cancellationToken);
}
