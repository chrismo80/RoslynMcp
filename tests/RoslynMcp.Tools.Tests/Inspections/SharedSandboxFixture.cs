using Xunit;

namespace RoslynMcp.Tools.Tests.Inspections;

[CollectionDefinition(CollectionName)]
public sealed class SharedSandboxToolsTestsCollection : ICollectionFixture<SharedSandboxFixture>
{
    public const string CollectionName = "ToolsTests";
}

public sealed class SharedSandboxFixture : IAsyncLifetime
{
    public SharedSandboxContext Context { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        Context = new SharedSandboxContext();
        await Context.InitializeAsync().ConfigureAwait(false);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Context is null)
            return;

        await Context.DisposeAsync().ConfigureAwait(false);
    }
}
