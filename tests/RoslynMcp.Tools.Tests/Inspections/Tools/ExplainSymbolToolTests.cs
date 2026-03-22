using Is.Assertions;
using RoslynMcp.Tools.Tests.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

public sealed class ExplainSymbolToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<RoslynMcp.Tools.Inspection.ExplainSymbol.Tool>(fixture, output), IClassFixture<SharedSandboxFixture>
{
    [Fact]
    public async Task Run_WithDocumentedMethod_IncludesStructuredDocumentation()
    {
        var result = await Sut.Run(CancellationToken.None, path: DocumentationPath, line: 72, column: 19);

        result.Error.IsNull();
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
    public async Task Run_WithUndocumentedMethod_OmitsDocumentation()
    {
        var result = await Sut.Run(CancellationToken.None, path: DocumentationPath, line: 86, column: 17);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("NoDocumentation");
        result.Documentation.IsNull();
    }

    [Fact]
    public async Task Run_WithSourcePosition_ReturnsExplanation()
    {
        var result = await Sut.Run(CancellationToken.None, path: AppOrchestratorPath, line: 6, column: 21);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("AppOrchestrator");

        result.RoleSummary.ShouldNotBeEmpty();
        result.RoleSummary.Contains("Key collaborators:", StringComparison.Ordinal).IsTrue();
        result.RoleSummary.Contains("IWorkItemOperation", StringComparison.Ordinal).IsTrue();
        result.RoleSummary.Contains("ProcessingSession", StringComparison.Ordinal).IsTrue();
        result.Signature.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Run_WhenNoSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.Run(CancellationToken.None);

        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_input");
    }
}
