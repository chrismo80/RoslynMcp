using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class FindUsagesToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<FindUsagesTool>(fixture, output)
{
    [Fact]
    public async Task FindUsagesAsync_WithSolutionScope_ReturnsOrderedReferences()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "solution");

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("IWorkItemOperation");
        result.TotalCount.Is(4);

        result.ReferenceFiles.ShouldMatchReferences(
            (Path.Combine("ProjectApp", "AppOrchestrator.cs"), 6),
            (Path.Combine("ProjectApp", "AppOrchestrator.cs"), 10),
            (Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 15),
            (Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 38));
    }

    [Fact]
    public async Task FindUsagesAsync_WithProjectScope_ExcludesCrossProjectReferences()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "project");

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.TotalCount.Is(0);
        result.ReferenceFiles.IsEmpty();
    }

    [Fact]
    public async Task FindUsagesAsync_WithDocumentScopeAndValidPath_ReturnsOnlyDocumentReferences()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "document", path: AppOrchestratorPath);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.TotalCount.Is(2);

        result.ReferenceFiles.ShouldMatchReferences(
            (Path.Combine("ProjectApp", "AppOrchestrator.cs"), 6),
            (Path.Combine("ProjectApp", "AppOrchestrator.cs"), 10));
    }

    [Fact]
    public async Task FindUsagesAsync_WithDocumentScopeWithoutPath_ReturnsValidationError()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "document");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidRequest);
        result.TotalCount.Is(0);
        result.ReferenceFiles.IsEmpty();
    }

    [Fact]
    public async Task FindUsagesAsync_WithDocumentScopeAndInvalidPath_ReturnsInvalidPathError()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "document", path: Path.Combine(TestSolutionDirectory, "ProjectApp", "Missing.cs"));

        result.Error.ShouldHaveCode(ErrorCodes.InvalidPath);
        result.Symbol.IsNull();
        result.TotalCount.Is(0);
        result.ReferenceFiles.IsEmpty();
    }

    [Fact]
    public async Task FindUsagesAsync_WithInvalidScope_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, "symbol-id", scope: "invalid");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidRequest);
        result.TotalCount.Is(0);
        result.ReferenceFiles.IsEmpty();
    }

    [Theory]
    [InlineData("not-a-real-symbol-id", ErrorCodes.SymbolNotFound)]
    [InlineData("   ", ErrorCodes.InvalidInput)]
    public async Task FindUsagesAsync_WithUnresolvedOrInvalidSymbolId_ReturnsExpectedError(string symbolId, string expectedErrorCode)
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "solution");

        result.Error.ShouldHaveCode(expectedErrorCode);
        result.Symbol.IsNull();
        result.TotalCount.Is(0);
        result.ReferenceFiles.IsEmpty();
    }

    private async Task<string> ResolveWorkItemOperationSymbolIdAsync()
    {
        var resolver = Context.GetRequiredService<ResolveSymbolTool>();
        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: ContractsPath, line: 31, column: 24);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();

        return resolved.Symbol!.SymbolId;
    }
}

