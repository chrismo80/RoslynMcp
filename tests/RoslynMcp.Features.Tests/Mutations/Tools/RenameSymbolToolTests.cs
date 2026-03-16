using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Features;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using RoslynMcp.Features.Tools.Mutations;
using RoslynMcp.Infrastructure;
using RoslynMcp.Infrastructure.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Mutations.Tools;

public sealed class RenameSymbolToolTests(ITestOutputHelper output)
    : IsolatedToolTests<RenameSymbolTool>(output)
{
    [Fact]
    public async Task RenameSymbolAsync_WithIsolatedSandbox_RenamesInterfaceAcrossSolution()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var contractsPath = context.GetFilePath("ProjectCore", "Contracts");

        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: contractsPath, line: 31, column: 24);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();

        var result = await sut.ExecuteAsync(CancellationToken.None, resolved.Symbol!.SymbolId, "IRenamedWorkItemOperation");

        result.Error.ShouldBeNone();
        result.ChangedDocumentCount.Is(3);
        result.RenamedSymbolId.IsNotNull();
        result.RenamedSymbolId!.ShouldBeExternalSymbolId();
        result.RenamedSymbolId.Is(resolved.Symbol!.SymbolId);
        result.ChangedFiles.Count.Is(3);
        result.ChangedFiles[0].ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.ChangedFiles[1].ShouldEndWithPathSuffix(Path.Combine("ProjectCore", "Contracts.cs"));
        result.ChangedFiles[2].ShouldEndWithPathSuffix(Path.Combine("ProjectImpl", "WorkItemOperations.cs"));

        result.AffectedLocationFiles.ShouldContainAffectedLocation(Path.Combine("ProjectCore", "Contracts.cs"), 31);
        result.AffectedLocationFiles.ShouldContainAffectedLocation(Path.Combine("ProjectApp", "AppOrchestrator.cs"), 6);
        result.AffectedLocationFiles.ShouldContainAffectedLocation(Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 15);

        var renamed = await resolver.ExecuteAsync(CancellationToken.None,
            qualifiedName: "ProjectCore.IRenamedWorkItemOperation",
            projectName: "ProjectCore");

        renamed.Error.ShouldBeNone();
        renamed.IsAmbiguous.IsFalse();
        renamed.Candidates.IsEmpty();
        renamed.Symbol.ShouldMatchResolvedSymbol("IRenamedWorkItemOperation", "NamedType", Path.Combine("ProjectCore", "Contracts.cs"));
        renamed.Symbol!.SymbolId.Is(result.RenamedSymbolId);

        var original = await resolver.ExecuteAsync(CancellationToken.None,
            qualifiedName: "ProjectCore.IWorkItemOperation",
            projectName: "ProjectCore");

        original.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
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
    public async Task RenameSymbolAsync_WithUnknownSymbolId_ReturnsSymbolNotFoundWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.ExecuteAsync(CancellationToken.None, "not-a-real-symbol-id", "IRenamedWorkItemOperation");

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.RenamedSymbolId.IsNull();
        result.ChangedDocumentCount.Is(0);
        result.AffectedLocationFiles.IsEmpty();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task RenameSymbolAsync_WithConflictingName_ReturnsRenameConflictWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var contractsPath = context.GetFilePath("ProjectCore", "Contracts");

        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: contractsPath, line: 31, column: 24);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();

        var result = await sut.ExecuteAsync(CancellationToken.None, resolved.Symbol!.SymbolId, "IFactory");

        result.Error.ShouldHaveCode(ErrorCodes.RenameConflict);
        result.RenamedSymbolId.IsNull();
        result.ChangedDocumentCount.Is(0);
        result.AffectedLocationFiles.IsEmpty();
        result.ChangedFiles.IsEmpty();

        var contractsText = await File.ReadAllTextAsync(contractsPath);
        contractsText.Contains("IWorkItemOperation", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task RenameSymbolAsync_CanRenameBackWithoutManualReload()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var contractsPath = context.GetFilePath("ProjectCore", "Contracts");

        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: contractsPath, line: 31, column: 24);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();

        var renamed = await sut.ExecuteAsync(CancellationToken.None, resolved.Symbol!.SymbolId, "IRenamedWorkItemOperation");

        renamed.Error.ShouldBeNone();
        renamed.RenamedSymbolId.IsNotNull();

        var reverted = await sut.ExecuteAsync(CancellationToken.None, renamed.RenamedSymbolId!, "IWorkItemOperation");

        reverted.Error.ShouldBeNone();
        reverted.RenamedSymbolId.IsNotNull();

        var finalResolution = await resolver.ExecuteAsync(CancellationToken.None,
            qualifiedName: "ProjectCore.IWorkItemOperation",
            projectName: "ProjectCore");

        finalResolution.Error.ShouldBeNone();
        finalResolution.Symbol.IsNotNull();
        finalResolution.Symbol!.DisplayName.Is("IWorkItemOperation");
    }

    [Fact]
    public async Task RenameSymbolAsync_WhenFirstApplyFailsAndReloadRetries_ReturnsUsableRenamedSymbolId()
    {
        await using var context = await RetryOnFirstApplySandboxContext.CreateAsync();
        var sut = context.GetRequiredService<RenameSymbolTool>();
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var contractsPath = context.GetFilePath("ProjectCore", "Contracts");

        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: contractsPath, line: 31, column: 24);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();

        var renamed = await sut.ExecuteAsync(CancellationToken.None, resolved.Symbol!.SymbolId, "IRenamedWorkItemOperation");

        renamed.Error.ShouldBeNone();
        renamed.RenamedSymbolId.IsNotNull();
        context.ApplyInterceptor.ApplyAttempts.Is(2);

        var reverted = await sut.ExecuteAsync(CancellationToken.None, renamed.RenamedSymbolId!, "IWorkItemOperation");

        reverted.Error.ShouldBeNone();
        reverted.RenamedSymbolId.IsNotNull();

        var finalResolution = await resolver.ExecuteAsync(CancellationToken.None,
            qualifiedName: "ProjectCore.IWorkItemOperation",
            projectName: "ProjectCore");

        finalResolution.Error.ShouldBeNone();
        finalResolution.Symbol.IsNotNull();
        finalResolution.Symbol!.SymbolId.Is(reverted.RenamedSymbolId);
    }
}

file static class AssertionExtensions
{
    extension(IReadOnlyList<AffectedFileLocations> locations)
    {
        internal void ShouldContainAffectedLocation(string expectedFileName, int expectedLine)
        {
            locations.Any(location => location.FilePath.HasPathSuffix(expectedFileName) && location.Locations.Any(position => position.Line == expectedLine)).IsTrue();
        }
    }
}

file sealed class RetryOnFirstApplySandboxContext : SandboxContext
{
    private RetryOnFirstApplySandboxContext()
    { }

    public FirstApplyFailsSolutionSessionService ApplyInterceptor => GetRequiredService<FirstApplyFailsSolutionSessionService>();

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

    protected override ServiceProvider CreateServiceProvider() => new ServiceCollection()
        .AddInfrastructure()
        .AddSingleton<FirstApplyFailsSolutionSessionService>()
        .AddSingleton<ISolutionSessionService>(provider => provider.GetRequiredService<FirstApplyFailsSolutionSessionService>())
        .AddSingleton<IRoslynSolutionAccessor>(provider => provider.GetRequiredService<FirstApplyFailsSolutionSessionService>())
        .AddImplementations<Tool>()
        .BuildServiceProvider();
}

file sealed class FirstApplyFailsSolutionSessionService(RoslynSolutionSessionService inner)
    : ISolutionSessionService, IRoslynSolutionAccessor
{
    private int _remainingApplyFailures = 1;

    public int ApplyAttempts { get; private set; }

    public Task<DiscoverSolutionsResult> DiscoverSolutionsAsync(DiscoverSolutionsRequest request, CancellationToken ct)
        => inner.DiscoverSolutionsAsync(request, ct);

    public Task<SelectSolutionResult> SelectSolutionAsync(SelectSolutionRequest request, CancellationToken ct)
        => inner.SelectSolutionAsync(request, ct);

    public Task<ReloadSolutionResult> ReloadSolutionAsync(ReloadSolutionRequest request, CancellationToken ct)
        => inner.ReloadSolutionAsync(request, ct);

    public Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct)
        => inner.GetCurrentSolutionAsync(ct);

    public Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct)
        => inner.GetWorkspaceVersionAsync(ct);

    public Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct)
    {
        ApplyAttempts++;
        if (Interlocked.Exchange(ref _remainingApplyFailures, 0) == 1)
        {
            return Task.FromResult((false, (ErrorInfo?)new ErrorInfo(ErrorCodes.InternalError, "Injected apply failure for retry coverage.")));
        }

        return inner.TryApplySolutionAsync(solution, ct);
    }
}
