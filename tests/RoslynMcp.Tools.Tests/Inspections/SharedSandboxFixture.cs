using Xunit;

namespace RoslynMcp.Tools.Tests.Inspections;

[CollectionDefinition(SharedSandboxCollections.CoreCollectionName)]
public sealed class SharedSandboxCoreToolsTestsCollection : ICollectionFixture<SharedSandboxFixture>
{
}

[CollectionDefinition(SharedSandboxCollections.AnalysisCollectionName)]
public sealed class SharedSandboxAnalysisToolsTestsCollection : ICollectionFixture<SharedSandboxFixture>
{
}

[CollectionDefinition(SharedSandboxCollections.GraphCollectionName)]
public sealed class SharedSandboxGraphToolsTestsCollection : ICollectionFixture<SharedSandboxFixture>
{
}

public static class SharedSandboxCollections
{
    public const string CoreCollectionName = "ToolsTests.Core";
    public const string AnalysisCollectionName = "ToolsTests.Analysis";
    public const string GraphCollectionName = "ToolsTests.Graph";
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