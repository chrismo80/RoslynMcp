namespace RoslynMcp.Features.Tests;

public sealed class IsolatedSandboxContext : SandboxContext
{
    private IsolatedSandboxContext()
    { }

    public static async Task<IsolatedSandboxContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        var context = new IsolatedSandboxContext();
        try
        {
            var sandbox = TestSolutionSandbox.Create(context.CanonicalTestSolutionDirectory);
            await context.InitializeSandboxAsync(sandbox, cancellationToken).ConfigureAwait(false);
            return context;
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
