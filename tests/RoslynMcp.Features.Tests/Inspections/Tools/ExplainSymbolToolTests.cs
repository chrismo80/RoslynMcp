using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class ExplainSymbolToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<ExplainSymbolTool>(fixture, output)
{
    [Fact]
    public async Task ExplainSymbolAsync_WithDocumentedMethod_IncludesStructuredDocumentation()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: DocumentationPath, line: 72, column: 19);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("MixedReferences");
        result.Documentation.IsNotNull();
        result.Documentation!.Summary.Is("Method with both x and System.String references.");
        result.Documentation.Returns.Is("The string representation.");
        result.Documentation.Parameters.IsNotNull();
        result.Documentation.Parameters!.Count.Is(1);
        result.Documentation.Parameters[0].Name.Is("x");
        result.Documentation.Parameters[0].Description.Is("The x parameter.");
    }

    [Fact]
    public async Task ExplainSymbolAsync_WithUndocumentedMethod_OmitsDocumentation()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: DocumentationPath, line: 86, column: 17);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("NoDocumentation");
        result.Documentation.IsNull();
    }

    [Fact]
    public async Task ExplainSymbolAsync_WithSourcePosition_ReturnsExplanation()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 6, column: 21);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("AppOrchestrator");

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
