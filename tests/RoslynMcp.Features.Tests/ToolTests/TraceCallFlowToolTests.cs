using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class TraceCallFlowToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<TraceCallFlowTool>(fixture, output)
{
    [Fact]
    public async Task TraceFlowAsync_WithResolvedRunAsyncSymbol_ReturnsStableDownstreamEdges()
    {
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var startAsync = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), line: 5, column: 23);
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 53, column: 35);
        var calculate = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "CodeSmells"), line: 23, column: 16);
        var stop = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), line: 12, column: 17);
        var changeState = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.State"), line: 11, column: 18);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: runAsync.Symbol!.SymbolId, direction: "downstream", depth: 2);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.RootSymbol!.Name.Is("RunAsync");
        result.RootSymbol.Kind.Is("Method");
        result.Direction.Is("downstream");
        result.Depth.Is(2);
        result.Edges.Count.Is(10);

        AssertEdge(result.Edges, runAsync.Symbol.SymbolId, startAsync.Symbol!.SymbolId, "ProjectApp\\AppOrchestrator.cs", 20);
        AssertEdge(result.Edges, runAsync.Symbol.SymbolId, executeFlowAsync.Symbol!.SymbolId, "ProjectApp\\AppOrchestrator.cs", 22);
        AssertEdge(result.Edges, runAsync.Symbol.SymbolId, calculate.Symbol!.SymbolId, "ProjectApp\\AppOrchestrator.cs", 24);
        AssertEdge(result.Edges, runAsync.Symbol.SymbolId, stop.Symbol!.SymbolId, "ProjectApp\\AppOrchestrator.cs", 26);
        AssertEdge(result.Edges, startAsync.Symbol.SymbolId, changeState.Symbol!.SymbolId, "ProjectImpl\\ProcessingSession.Lifecycle.cs", 7);
        AssertEdge(result.Edges, stop.Symbol.SymbolId, changeState.Symbol.SymbolId, "ProjectImpl\\ProcessingSession.Lifecycle.cs", 14);

        result.Transitions.Count.Is(1);
        AssertTransition(result.Transitions, "unknown", "unknown", 10);
    }

    [Fact]
    public async Task TraceFlowAsync_WithExecuteFlowAsyncSymbol_ReturnsStableUpstreamEdgesAcrossDepths()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 53, column: 35);
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var runFastAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 77, column: 41);
        var runSafeAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 82, column: 41);

        var depthOne = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.Symbol!.SymbolId, direction: "upstream", depth: 1);
        var depthTwo = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.Symbol.SymbolId, direction: "upstream", depth: 2);

        depthOne.Error.ShouldBeNone();
        depthOne.RootSymbol.IsNotNull();
        depthOne.RootSymbol!.Name.Is("ExecuteFlowAsync");
        depthOne.Direction.Is("upstream");
        depthOne.Depth.Is(1);
        depthOne.Edges.Count.Is(1);
        AssertEdge(depthOne.Edges, runAsync.Symbol!.SymbolId, executeFlowAsync.Symbol.SymbolId, "ProjectApp\\AppOrchestrator.cs", 22);

        depthTwo.Error.ShouldBeNone();
        depthTwo.Direction.Is("upstream");
        depthTwo.Depth.Is(2);
        depthTwo.Edges.Count.Is(3);
        AssertEdge(depthTwo.Edges, runAsync.Symbol.SymbolId, executeFlowAsync.Symbol.SymbolId, "ProjectApp\\AppOrchestrator.cs", 22);
        AssertEdge(depthTwo.Edges, runFastAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, "ProjectApp\\AppOrchestrator.cs", 79);
        AssertEdge(depthTwo.Edges, runSafeAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, "ProjectApp\\AppOrchestrator.cs", 84);
        depthTwo.Transitions.Count.Is(1);
        AssertTransition(depthTwo.Transitions, "unknown", "unknown", 3);
    }

    [Fact]
    public async Task TraceFlowAsync_WithExecuteFlowAsyncSymbolAndBothDirection_ReturnsIncomingAndOutgoingEdges()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 53, column: 35);
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var runFastAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 77, column: 41);
        var runSafeAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 82, column: 41);
        var operationExecuteAsync = await ResolveSymbolAsync(ContractsPath, line: 18, column: 19);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.Symbol!.SymbolId, direction: "both", depth: 2);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.RootSymbol!.Name.Is("ExecuteFlowAsync");
        result.Direction.Is("both");
        result.Depth.Is(2);
        result.Edges.Count.Is(4);

        AssertEdge(result.Edges, runAsync.Symbol!.SymbolId, executeFlowAsync.Symbol.SymbolId, "ProjectApp\\AppOrchestrator.cs", 22);
        AssertEdge(result.Edges, runFastAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, "ProjectApp\\AppOrchestrator.cs", 79);
        AssertEdge(result.Edges, runSafeAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, "ProjectApp\\AppOrchestrator.cs", 84);
        AssertEdge(result.Edges, executeFlowAsync.Symbol.SymbolId, operationExecuteAsync.Symbol!.SymbolId, "ProjectApp\\AppOrchestrator.cs", 55);
        result.Transitions.Count.Is(1);
        AssertTransition(result.Transitions, "unknown", "unknown", 4);
    }

    [Fact]
    public async Task TraceFlowAsync_WithPathLineAndColumnSelector_ReturnsResolvedRootAndDirectDownstreamEdge()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 53, column: 35, direction: "downstream", depth: 1);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.RootSymbol!.Name.Is("ExecuteFlowAsync");
        result.RootSymbol.Kind.Is("Method");
        result.RootSymbol.DeclarationLocation.FilePath.IsEndingWith("ProjectApp\\AppOrchestrator.cs");
        result.RootSymbol.DeclarationLocation.Line.Is(53);
        result.Direction.Is("downstream");
        result.Depth.Is(1);
        result.Edges.Count.Is(1);
        result.Edges[0].FromSymbolId.Is(result.RootSymbol.SymbolId);
        result.Edges[0].Location.FilePath.IsEndingWith("ProjectApp\\AppOrchestrator.cs");
        result.Edges[0].Location.Line.Is(55);
    }

    [Fact]
    public async Task TraceFlowAsync_WithInvalidDirection_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: "symbol-id", direction: "sideways");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task TraceFlowAsync_WithUnresolvedSymbolId_ReturnsSymbolNotFound()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: "ProjectApp:DoesNotExist", direction: "downstream");

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.RootSymbol.IsNull();
        result.Edges.IsEmpty();
    }

    [Fact]
    public async Task TraceFlowAsync_WithoutSelector_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, direction: "downstream");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
        result.RootSymbol.IsNull();
        result.Edges.IsEmpty();
    }

    private async Task<ResolvedSymbolSummaryResult> ResolveSymbolAsync(string path, int line, int column)
    {
        var resolver = Fixture.GetRequiredService<ResolveSymbolTool>();
        var result = await resolver.ExecuteAsync(CancellationToken.None, path: path, line: line, column: column);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        return new ResolvedSymbolSummaryResult(result.Symbol!);
    }

    private static void AssertEdge(IReadOnlyList<CallEdge> edges, string fromSymbolId, string toSymbolId, string expectedFileSuffix, int expectedLine)
    {
        edges.Any(edge =>
            edge.FromSymbolId == fromSymbolId &&
            edge.ToSymbolId == toSymbolId &&
            edge.Location.FilePath.IsEndingWith(expectedFileSuffix) &&
            edge.Location.Line == expectedLine).IsTrue();
    }

    private static void AssertTransition(IReadOnlyList<FlowTransition> transitions, string fromProject, string toProject, int expectedCount)
    {
        transitions.Any(transition =>
            transition.FromProject == fromProject &&
            transition.ToProject == toProject &&
            transition.Count == expectedCount).IsTrue();
    }

    private sealed record ResolvedSymbolSummaryResult(ResolvedSymbolSummary Symbol);
}
