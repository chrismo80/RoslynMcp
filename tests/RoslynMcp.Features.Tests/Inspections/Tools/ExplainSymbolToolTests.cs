using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class ExplainSymbolToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<ExplainSymbolTool>(fixture, output)
{
    [Fact]
    public async Task ExplainSymbolAsync_WithSourcePosition_ReturnsExplanation()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 6, column: 21);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Name.Is("AppOrchestrator");

        result.RoleSummary.ShouldNotBeEmpty();
        result.RoleSummary.Contains("Key collaborators:", StringComparison.Ordinal).IsTrue();
        result.RoleSummary.Contains("IWorkItemOperation", StringComparison.Ordinal).IsTrue();
        result.RoleSummary.Contains("ProcessingSession", StringComparison.Ordinal).IsTrue();
        result.Signature.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ExplainSymbolAsync_WhenNoSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}
