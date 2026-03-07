using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class TraceCallFlowToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<TraceCallFlowTool>(fixture, output)
{
    [Fact]
    public async Task TraceFlowAsync_WithResolvedRunAsyncSymbol_ReturnsStableDownstreamEdges()
    {
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var startAsync = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), line: 5, column: 23);
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 54, column: 35);
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

        AssertEdge(result.Edges, runAsync.Symbol.SymbolId, startAsync.Symbol!.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 20);
        AssertEdge(result.Edges, runAsync.Symbol.SymbolId, executeFlowAsync.Symbol!.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);
        AssertEdge(result.Edges, runAsync.Symbol.SymbolId, calculate.Symbol!.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 25);
        AssertEdge(result.Edges, runAsync.Symbol.SymbolId, stop.Symbol!.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 27);
        AssertEdge(result.Edges, startAsync.Symbol.SymbolId, changeState.Symbol!.SymbolId, Path.Combine("ProjectImpl", "ProcessingSession.Lifecycle.cs"), 7);
        AssertEdge(result.Edges, stop.Symbol.SymbolId, changeState.Symbol.SymbolId, Path.Combine("ProjectImpl", "ProcessingSession.Lifecycle.cs"), 14);

        result.Transitions.Any(static transition => transition.FromProject == "unknown" || transition.ToProject == "unknown").IsFalse();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectApp", ToProject: "ProjectCore" }).IsTrue();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectApp", ToProject: "ProjectImpl" }).IsTrue();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectApp", ToProject: "ProjectApp" }).IsTrue();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectImpl", ToProject: "ProjectImpl" }).IsTrue();
    }

    [Fact]
    public async Task TraceFlowAsync_WithExecuteFlowAsyncSymbol_ReturnsStableUpstreamEdgesAcrossDepths()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 54, column: 35);
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var runFastAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 78, column: 41);
        var runSafeAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 83, column: 41);

        var depthOne = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.Symbol!.SymbolId, direction: "upstream", depth: 1);
        var depthTwo = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.Symbol.SymbolId, direction: "upstream", depth: 2);

        depthOne.Error.ShouldBeNone();
        depthOne.RootSymbol.IsNotNull();
        depthOne.RootSymbol!.Name.Is("ExecuteFlowAsync");
        depthOne.Direction.Is("upstream");
        depthOne.Depth.Is(1);
        depthOne.Edges.Count.Is(1);
        AssertEdge(depthOne.Edges, runAsync.Symbol!.SymbolId, executeFlowAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);

        depthTwo.Error.ShouldBeNone();
        depthTwo.Direction.Is("upstream");
        depthTwo.Depth.Is(2);
        depthTwo.Edges.Count.Is(3);
        AssertEdge(depthTwo.Edges, runAsync.Symbol.SymbolId, executeFlowAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);
        AssertEdge(depthTwo.Edges, runFastAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 80);
        AssertEdge(depthTwo.Edges, runSafeAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 85);
        depthTwo.Transitions.Any(static transition => transition.FromProject == "unknown" || transition.ToProject == "unknown").IsFalse();
        AssertTransition(depthTwo.Transitions, "ProjectApp", "ProjectApp", 3);
    }

    [Fact]
    public async Task TraceFlowAsync_WithExecuteFlowAsyncSymbolAndBothDirection_ReturnsIncomingAndOutgoingEdges()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 54, column: 35);
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var runFastAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 78, column: 41);
        var runSafeAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 83, column: 41);
        var operationExecuteAsync = await ResolveSymbolAsync(ContractsPath, line: 18, column: 19);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.Symbol!.SymbolId, direction: "both", depth: 2);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.RootSymbol!.Name.Is("ExecuteFlowAsync");
        result.Direction.Is("both");
        result.Depth.Is(2);
        result.Edges.Count.Is(4);

        AssertEdge(result.Edges, runAsync.Symbol!.SymbolId, executeFlowAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);
        AssertEdge(result.Edges, runFastAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 80);
        AssertEdge(result.Edges, runSafeAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 85);
        AssertEdge(result.Edges, executeFlowAsync.Symbol.SymbolId, operationExecuteAsync.Symbol!.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 56);
        result.Transitions.Any(static transition => transition.FromProject == "unknown" || transition.ToProject == "unknown").IsFalse();
        AssertTransition(result.Transitions, "ProjectApp", "ProjectApp", 3);
        AssertTransition(result.Transitions, "ProjectApp", "ProjectCore", 1);
    }

    [Fact]
    public async Task TraceFlowAsync_WithPathLineAndColumnSelector_ReturnsResolvedRootAndDirectDownstreamEdge()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 54, column: 35, direction: "downstream", depth: 1);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.RootSymbol!.Name.Is("ExecuteFlowAsync");
        result.RootSymbol.Kind.Is("Method");
        result.RootSymbol.DeclarationLocation.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.RootSymbol.DeclarationLocation.Line.Is(54);
        result.Direction.Is("downstream");
        result.Depth.Is(1);
        result.Edges.Count.Is(1);
        result.Edges[0].FromSymbolId.Is(result.RootSymbol.SymbolId);
        result.Edges[0].Location.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.Edges[0].Location.Line.Is(56);
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
        var resolver = Context.GetRequiredService<ResolveSymbolTool>();
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
            edge.Location.FilePath.HasPathSuffix(expectedFileSuffix) &&
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
