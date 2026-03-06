using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class RenameSymbolToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<RenameSymbolTool>(fixture, output)
{
    [Theory]
    [InlineData("not-a-real-symbol-id", "NewName", ErrorCodes.SymbolNotFound)]
    [InlineData("   ", "NewName", ErrorCodes.InvalidInput)]
    [InlineData("valid-symbol-id", "", ErrorCodes.InvalidInput)]
    [InlineData("valid-symbol-id", "   ", ErrorCodes.InvalidInput)]
    public async Task RenameSymbolAsync_WithInvalidInputs_ReturnsExpectedError(string symbolId, string newName, string expectedErrorCode)
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, newName);

        result.Error.ShouldHaveCode(expectedErrorCode);
        result.RenamedSymbolId.IsNull();
        result.ChangedDocumentCount.Is(0);
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task RenameSymbolAsync_WithInvalidNewName_ReturnsValidationError()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, "123Invalid");

        result.Error.IsNotNull();
        result.RenamedSymbolId.IsNull();
        result.ChangedDocumentCount.Is(0);
    }

    [Fact]
    public async Task RenameSymbolAsync_WithValidSymbol_RenamesSuccessfully()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();
        var newName = "IRenamedWorkItemOperation";

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, newName);

        result.Error.ShouldBeNone();
        result.RenamedSymbolId.IsNotNull();
        result.ChangedDocumentCount.IsGreaterThan(0);
        result.ChangedFiles.IsNotEmpty();
        result.AffectedLocations.IsNotEmpty();
        
        Trace($"Renamed to: {result.RenamedSymbolId}");
        Trace($"Changed documents: {result.ChangedDocumentCount}");
        Trace($"Changed files: {string.Join(", ", result.ChangedFiles)}");
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
