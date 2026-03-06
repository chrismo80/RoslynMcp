using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class FindUsagesToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<FindUsagesTool>(fixture, output)
{
    private string ContractsPath => Path.Combine(TestSolutionDirectory, "ProjectCore", "Contracts.cs");

    private string AppOrchestratorPath => Path.Combine(TestSolutionDirectory, "ProjectApp", "AppOrchestrator.cs");

    [Fact]
    public async Task FindUsagesAsync_WithSolutionScope_ReturnsOrderedReferences()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "solution");

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Name.Is("IWorkItemOperation");
        result.TotalCount.Is(4);
        
        result.References.ShouldMatchReferences(
            ("ProjectApp\\AppOrchestrator.cs", 6),
            ("ProjectApp\\AppOrchestrator.cs", 10),
            ("ProjectImpl\\WorkItemOperations.cs", 15),
            ("ProjectImpl\\WorkItemOperations.cs", 38));
    }

    [Fact]
    public async Task FindUsagesAsync_WithProjectScope_ExcludesCrossProjectReferences()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "project");

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.TotalCount.Is(0);
        result.References.IsEmpty();
    }

    [Fact]
    public async Task FindUsagesAsync_WithDocumentScopeAndValidPath_ReturnsOnlyDocumentReferences()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "document", path: AppOrchestratorPath);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.TotalCount.Is(2);
        
        result.References.ShouldMatchReferences(
            ("ProjectApp\\AppOrchestrator.cs", 6),
            ("ProjectApp\\AppOrchestrator.cs", 10));
    }

    [Fact]
    public async Task FindUsagesAsync_WithDocumentScopeWithoutPath_ReturnsValidationError()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "document");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidRequest);
        result.TotalCount.Is(0);
        result.References.IsEmpty();
    }

    [Fact]
    public async Task FindUsagesAsync_WithDocumentScopeAndInvalidPath_ReturnsInvalidPathError()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, scope: "document", path: Path.Combine(TestSolutionDirectory, "ProjectApp", "Missing.cs"));

        result.Error.ShouldHaveCode(ErrorCodes.InvalidPath);
        result.Symbol.IsNull();
        result.TotalCount.Is(0);
        result.References.IsEmpty();
    }

    [Fact]
    public async Task FindUsagesAsync_WithInvalidScope_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, "symbol-id", scope: "invalid");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidRequest);
        result.TotalCount.Is(0);
        result.References.IsEmpty();
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
        result.References.IsEmpty();
    }
    
    private async Task<string> ResolveWorkItemOperationSymbolIdAsync()
    {
        var resolver = Fixture.GetRequiredService<ResolveSymbolTool>();
        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: ContractsPath, line: 31, column: 24);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();

        return resolved.Symbol!.SymbolId;
    }
}
