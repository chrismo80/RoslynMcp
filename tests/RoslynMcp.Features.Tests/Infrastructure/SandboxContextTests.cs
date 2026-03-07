using Is.Assertions;
using Xunit;

namespace RoslynMcp.Features.Tests;

public sealed class SandboxContextTests
{
    [Fact]
    public async Task InitializeSandboxAsync_WhenSolutionLoadFails_CleansUpSandboxAndServices()
    {
        var context = new TestSandboxContext();
        var sandbox = TestSolutionSandbox.Create(context.CanonicalTestSolutionDirectory);
        var sandboxRoot = sandbox.SandboxRoot;

        File.Delete(sandbox.SolutionPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.InitializeForTestAsync(sandbox));

        Directory.Exists(sandboxRoot).IsFalse();
        await context.DisposeAsync();
    }

    private sealed class TestSandboxContext : SandboxContext
    {
        public Task InitializeForTestAsync(TestSolutionSandbox sandbox)
            => InitializeSandboxAsync(sandbox);
    }
}
