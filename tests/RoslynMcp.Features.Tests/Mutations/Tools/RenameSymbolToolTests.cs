using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
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
        result.RenamedSymbolId!.ShouldNotBeEmpty();
        result.ChangedFiles.Count.Is(3);
        result.ChangedFiles[0].ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.ChangedFiles[1].ShouldEndWithPathSuffix(Path.Combine("ProjectCore", "Contracts.cs"));
        result.ChangedFiles[2].ShouldEndWithPathSuffix(Path.Combine("ProjectImpl", "WorkItemOperations.cs"));

        ShouldContainAffectedLocation(result.AffectedLocations, Path.Combine("ProjectCore", "Contracts.cs"), 31);
        ShouldContainAffectedLocation(result.AffectedLocations, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 6);
        ShouldContainAffectedLocation(result.AffectedLocations, Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 15);

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
        result.AffectedLocations.IsEmpty();
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
        result.AffectedLocations.IsEmpty();
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

    private static void ShouldContainAffectedLocation(IReadOnlyList<SourceLocation> locations, string expectedFileName, int expectedLine)
    {
        locations.Any(location => location.FilePath.HasPathSuffix(expectedFileName) && location.Line == expectedLine).IsTrue();
    }
}
