using Microsoft.Extensions.DependencyInjection;
using Is.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Mutations.Tools;

public sealed class RenameSymbolToolTests(ITestOutputHelper output)
    : IsolatedToolTests<RoslynMcp.Tools.Mutation.RenameSymbol.Tool>(output)
{
    [Fact]
    public async Task Run_WithIsolatedSandbox_RenamesInterfaceAcrossSolution()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var contractsPath = context.GetFilePath("ProjectCore", "Contracts");

        var resolved = await resolver.Run(CancellationToken.None, path: contractsPath, line: 31, column: 24);

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();

        var result = await sut.Run(CancellationToken.None, resolved.Symbol!.SymbolId, "IRenamedWorkItemOperation");

        result.Error.IsNull();
        result.ChangedDocumentCount.Is(3);
        result.RenamedSymbolId.IsNotNull();
        result.RenamedSymbolId!.ShouldNotBeEmpty();
        result.RenamedSymbolId.Is(resolved.Symbol!.SymbolId);
        result.ChangedFiles.Count.Is(3);
        result.ChangedFiles[0].ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.ChangedFiles[1].ShouldEndWithPathSuffix(Path.Combine("ProjectCore", "Contracts.cs"));
        result.ChangedFiles[2].ShouldEndWithPathSuffix(Path.Combine("ProjectImpl", "WorkItemOperations.cs"));

        result.AffectedLocationFiles.ShouldContainAffectedLocation(Path.Combine("ProjectCore", "Contracts.cs"), 31);
        result.AffectedLocationFiles.ShouldContainAffectedLocation(Path.Combine("ProjectApp", "AppOrchestrator.cs"), 6);
        result.AffectedLocationFiles.ShouldContainAffectedLocation(Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 15);

        var renamed = await resolver.Run(CancellationToken.None, qualifiedName: "ProjectCore.IRenamedWorkItemOperation", projectName: "ProjectCore");

        renamed.Error.IsNull();
        renamed.IsAmbiguous.IsFalse();
        renamed.Candidates.IsEmpty();
        renamed.Symbol.ShouldMatchResolvedSymbol("IRenamedWorkItemOperation", "NamedType", Path.Combine("ProjectCore", "Contracts.cs"));
        renamed.Symbol!.SymbolId.Is(result.RenamedSymbolId);

        var original = await resolver.Run(CancellationToken.None, qualifiedName: "ProjectCore.IWorkItemOperation", projectName: "ProjectCore");

        original.Error.IsNotNull();
        original.Error!.Code.Is("symbol_not_found");
        original.Symbol.IsNull();

        var sandboxContractsText = await File.ReadAllTextAsync(contractsPath);
        sandboxContractsText.Contains("IRenamedWorkItemOperation", StringComparison.Ordinal).IsTrue();
        sandboxContractsText.Contains("IWorkItemOperation", StringComparison.Ordinal).IsFalse();

        var canonicalContractsText = await File.ReadAllTextAsync(Path.Combine(context.CanonicalTestSolutionDirectory, "ProjectCore", "Contracts.cs"));
        canonicalContractsText.Contains("IWorkItemOperation", StringComparison.Ordinal).IsTrue();
        canonicalContractsText.Contains("IRenamedWorkItemOperation", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task CreateContextAsync_WithFreshSandbox_StartsFromUntouchedBaseline()
    {
        await using var context = await CreateContextAsync();

        var contractsText = await File.ReadAllTextAsync(context.GetFilePath("ProjectCore", "Contracts"));

        contractsText.Contains("IWorkItemOperation", StringComparison.Ordinal).IsTrue();
        contractsText.Contains("IRenamedWorkItemOperation", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task Run_WithUnknownSymbolId_ReturnsSymbolNotFoundWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.Run(CancellationToken.None, "not-a-real-symbol-id", "IRenamedWorkItemOperation");

        result.Error.IsNotNull();
        result.Error!.Code.Is("symbol_not_found");
        result.RenamedSymbolId.IsNull();
        result.ChangedDocumentCount.Is(0);
        result.AffectedLocationFiles.IsEmpty();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task Run_WithConflictingName_ReturnsRenameConflictWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var contractsPath = context.GetFilePath("ProjectCore", "Contracts");

        var resolved = await resolver.Run(CancellationToken.None, path: contractsPath, line: 31, column: 24);

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();

        var result = await sut.Run(CancellationToken.None, resolved.Symbol!.SymbolId, "IFactory");

        result.Error.IsNotNull();
        result.Error!.Code.Is("rename_conflict");
        result.RenamedSymbolId.IsNull();
        result.ChangedDocumentCount.Is(0);
        result.AffectedLocationFiles.IsEmpty();
        result.ChangedFiles.IsEmpty();

        var contractsText = await File.ReadAllTextAsync(contractsPath);
        contractsText.Contains("IWorkItemOperation", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task Run_CanRenameBackWithoutManualReload()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var contractsPath = context.GetFilePath("ProjectCore", "Contracts");

        var resolved = await resolver.Run(CancellationToken.None, path: contractsPath, line: 31, column: 24);

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();

        var renamed = await sut.Run(CancellationToken.None, resolved.Symbol!.SymbolId, "IRenamedWorkItemOperation");

        renamed.Error.IsNull();
        renamed.RenamedSymbolId.IsNotNull();

        var reverted = await sut.Run(CancellationToken.None, renamed.RenamedSymbolId!, "IWorkItemOperation");

        reverted.Error.IsNull();
        reverted.RenamedSymbolId.IsNotNull();

        var finalResolution = await resolver.Run(CancellationToken.None, qualifiedName: "ProjectCore.IWorkItemOperation", projectName: "ProjectCore");

        finalResolution.Error.IsNull();
        finalResolution.Symbol.IsNotNull();
        finalResolution.Symbol!.DisplayName.Is("IWorkItemOperation");
    }

    [Fact]
    public async Task Run_WhenFirstApplyFailsAndReloadRetries_ReturnsUsableRenamedSymbolId()
    {
        await using var context = await RetryOnFirstApplySandboxContext.CreateAsync();
        var sut = context.GetRequiredService<RoslynMcp.Tools.Mutation.RenameSymbol.Tool>();
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var contractsPath = context.GetFilePath("ProjectCore", "Contracts");

        var resolved = await resolver.Run(CancellationToken.None, path: contractsPath, line: 31, column: 24);

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();

        var renamed = await sut.Run(CancellationToken.None, resolved.Symbol!.SymbolId, "IRenamedWorkItemOperation");

        renamed.Error.IsNull();
        renamed.RenamedSymbolId.IsNotNull();
        context.ApplyInterceptor.ApplyAttempts.Is(2);

        var reverted = await sut.Run(CancellationToken.None, renamed.RenamedSymbolId!, "IWorkItemOperation");

        reverted.Error.IsNull();
        reverted.RenamedSymbolId.IsNotNull();

        var finalResolution = await resolver.Run(CancellationToken.None, qualifiedName: "ProjectCore.IWorkItemOperation", projectName: "ProjectCore");

        finalResolution.Error.IsNull();
        finalResolution.Symbol.IsNotNull();
        finalResolution.Symbol!.SymbolId.Is(reverted.RenamedSymbolId);
    }
}

file static class RenameAssertionExtensions
{
    extension(IReadOnlyList<RoslynMcp.Tools.Mutation.RenameSymbol.AffectedFileLocations> locations)
    {
        internal void ShouldContainAffectedLocation(string expectedFileName, int expectedLine)
        {
            locations.Any(location => location.FilePath.HasPathSuffix(expectedFileName) && location.Locations.Any(position => position.Line == expectedLine)).IsTrue();
        }
    }

    internal static void ShouldMatchResolvedSymbol(this RoslynMcp.Tools.Inspection.ResolveSymbol.ResolvedSymbol? symbol, string expectedDisplayName, string expectedKind, string expectedFileName)
    {
        symbol.IsNotNull();
        symbol!.DisplayName.Is(expectedDisplayName);
        symbol.Kind.Is(expectedKind);
        symbol.Location.IsNotNull();
        symbol.Location!.FilePath.ShouldEndWithPathSuffix(expectedFileName);
        symbol.SymbolId.ShouldNotBeEmpty();
    }
}

file sealed class RetryOnFirstApplySandboxContext : SandboxContext
{
    private RetryOnFirstApplySandboxContext()
    {
    }

    public FirstApplyFailsWorkspace ApplyInterceptor => GetRequiredService<FirstApplyFailsWorkspace>();

    public static async Task<RetryOnFirstApplySandboxContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        var context = new RetryOnFirstApplySandboxContext();
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

    protected override ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.WithRoslynMcp();
        services.AddSingleton<FirstApplyFailsWorkspace>();
        services.AddSingleton<RoslynMcp.Tools.Infrastructure.Services.Workspace>(provider => provider.GetRequiredService<FirstApplyFailsWorkspace>());
        return services.BuildServiceProvider();
    }
}

file sealed class FirstApplyFailsWorkspace : RoslynMcp.Tools.Infrastructure.Services.Workspace
{
    private int _remainingApplyFailures = 1;

    public int ApplyAttempts { get; private set; }

    internal override bool TryApplyChanges(RoslynMcp.Tools.Infrastructure.Session session, Microsoft.CodeAnalysis.Solution solution)
    {
        ApplyAttempts++;
        if (Interlocked.Exchange(ref _remainingApplyFailures, 0) == 1)
            return false;

        return base.TryApplyChanges(session, solution);
    }
}
