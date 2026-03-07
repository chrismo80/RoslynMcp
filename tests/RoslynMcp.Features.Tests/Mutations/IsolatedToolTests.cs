using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Mutations;

public abstract class IsolatedToolTests<TTool>(ITestOutputHelper output)
    where TTool : notnull
{
    protected Task<IsolatedSandboxContext> CreateContextAsync(CancellationToken cancellationToken = default)
        => IsolatedSandboxContext.CreateAsync(cancellationToken);

    protected static TTool GetSut(IsolatedSandboxContext context)
        => context.GetRequiredService<TTool>();

    protected void Trace(string message) =>
        output.WriteLine(typeof(TTool) + ": " + message);
}