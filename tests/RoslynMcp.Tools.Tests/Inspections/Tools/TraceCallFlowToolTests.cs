using System.Text.Json;
using System.Text.Json.Serialization;
using Is.Assertions;
using RoslynMcp.Tools.Tests.Inspections;
using RoslynMcp.Tools.Tests.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

public sealed class TraceCallFlowToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<RoslynMcp.Tools.Inspection.TraceCallFlow.Tool>(fixture, output), IClassFixture<SharedSandboxFixture>
{
    [Fact]
    public async Task Run_WithResolvedRunAsyncSymbol_ReturnsStableDownstreamEdges()
    {
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, 15, 44);
        var startAsync = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), 5, 23);
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, 54, 35);
        var calculate = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "CodeSmells"), 23, 16);
        var stop = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), 12, 17);
        var changeState = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.State"), 11, 18);

        var result = await Sut.Run(CancellationToken.None, symbolId: runAsync.SymbolId, direction: "downstream", depth: 2);

        result.Error.IsNull();
        result.Root.IsNotNull();
        result.Root!.Name.Is("RunAsync");
        result.Root.Kind.Is("Method");
        result.Direction.Is("downstream");
        result.Depth.Is(2);
        result.Edges.Count.Is(9);

        result.Edges.AssertEdge(runAsync.SymbolId, startAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 20);
        result.Edges.AssertEdge(runAsync.SymbolId, executeFlowAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);
        result.Edges.AssertEdge(runAsync.SymbolId, calculate.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 25);
        result.Edges.AssertEdge(runAsync.SymbolId, stop.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 27);
        result.Edges.AssertEdge(startAsync.SymbolId, changeState.SymbolId, Path.Combine("ProjectImpl", "ProcessingSession.Lifecycle.cs"), 7);
        result.Edges.AssertEdge(stop.SymbolId, changeState.SymbolId, Path.Combine("ProjectImpl", "ProcessingSession.Lifecycle.cs"), 14);

        var directEdge = result.Edges.GetEdge(runAsync.SymbolId, startAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 20);
        directEdge.Kind.Is("direct_static");
        directEdge.UncertaintyCategories.IsNull();
        result.PossibleTargetEdges.IsNull();

        result.Transitions!.Any(static transition => transition.FromProject == "unknown" || transition.ToProject == "unknown").IsFalse();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectApp", ToProject: "ProjectCore" }).IsTrue();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectApp", ToProject: "ProjectImpl" }).IsTrue();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectApp", ToProject: "ProjectApp" }).IsTrue();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectImpl", ToProject: "ProjectImpl" }).IsTrue();
    }

    [Fact]
    public async Task Run_WithExecuteFlowAsyncSymbol_ReturnsStableUpstreamEdgesAcrossDepths()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, 54, 35);
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, 15, 44);
        var runFastAsync = await ResolveSymbolAsync(AppOrchestratorPath, 78, 41);
        var runSafeAsync = await ResolveSymbolAsync(AppOrchestratorPath, 83, 41);

        var depthOne = await Sut.Run(CancellationToken.None, symbolId: executeFlowAsync.SymbolId, direction: "upstream", depth: 1);
        var depthTwo = await Sut.Run(CancellationToken.None, symbolId: executeFlowAsync.SymbolId, direction: "upstream", depth: 2);

        depthOne.Error.IsNull();
        depthOne.Root.IsNotNull();
        depthOne.Root!.Name.Is("ExecuteFlowAsync");
        depthOne.Direction.Is("upstream");
        depthOne.Depth.Is(1);
        depthOne.Edges.Count.Is(1);
        depthOne.Edges.AssertEdge(runAsync.SymbolId, executeFlowAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);

        depthTwo.Error.IsNull();
        depthTwo.Direction.Is("upstream");
        depthTwo.Depth.Is(2);
        depthTwo.Edges.Count.Is(3);
        depthTwo.Edges.AssertEdge(runAsync.SymbolId, executeFlowAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);
        depthTwo.Edges.AssertEdge(runFastAsync.SymbolId, runAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 80);
        depthTwo.Edges.AssertEdge(runSafeAsync.SymbolId, runAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 85);
        depthTwo.Transitions!.Any(static transition => transition.FromProject == "unknown" || transition.ToProject == "unknown").IsFalse();
        depthTwo.Transitions.AssertTransition("ProjectApp", "ProjectApp", 3);
    }

    [Fact]
    public async Task Run_WithExecuteFlowAsyncSymbolAndBothDirection_ReturnsIncomingAndOutgoingEdges()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, 54, 35);
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, 15, 44);
        var runFastAsync = await ResolveSymbolAsync(AppOrchestratorPath, 78, 41);
        var runSafeAsync = await ResolveSymbolAsync(AppOrchestratorPath, 83, 41);
        var operationExecuteAsync = await ResolveSymbolAsync(ContractsPath, 18, 19);

        var result = await Sut.Run(CancellationToken.None, symbolId: executeFlowAsync.SymbolId, direction: "both", depth: 2);

        result.Error.IsNull();
        result.Root.IsNotNull();
        result.Root!.Name.Is("ExecuteFlowAsync");
        result.Direction.Is("both");
        result.Depth.Is(2);
        result.Edges.Count.Is(4);

        result.Edges.AssertEdge(runAsync.SymbolId, executeFlowAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);
        result.Edges.AssertEdge(runFastAsync.SymbolId, runAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 80);
        result.Edges.AssertEdge(runSafeAsync.SymbolId, runAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 85);
        result.Edges.AssertEdge(executeFlowAsync.SymbolId, operationExecuteAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 56);

        var dispatchEdge = result.Edges.GetEdge(executeFlowAsync.SymbolId, operationExecuteAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 56);
        dispatchEdge.Kind.Is("direct_static");
        dispatchEdge.UncertaintyCategories.IsNotNull();
        dispatchEdge.UncertaintyCategories!.Any(static uncertainty => uncertainty == "interface_dispatch").IsTrue();
        result.PossibleTargetEdges.IsNull();

        result.Transitions!.Any(static transition => transition.FromProject == "unknown" || transition.ToProject == "unknown").IsFalse();
        result.Transitions.AssertTransition("ProjectApp", "ProjectApp", 3);
        result.Transitions.AssertTransition("ProjectApp", "ProjectCore", 1);
    }

    [Fact]
    public async Task Run_WithPathLineAndColumnSelector_ReturnsResolvedRootAndDirectDownstreamEdge()
    {
        var result = await Sut.Run(CancellationToken.None, path: AppOrchestratorPath, line: 54, column: 35, direction: "downstream", depth: 1);

        result.Error.IsNull();
        result.Root.IsNotNull();
        result.Root!.Name.Is("ExecuteFlowAsync");
        result.Root.Kind.Is("Method");
        result.Root.Location!.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.Root.Location.Line.Is(54);
        result.Direction.Is("downstream");
        result.Depth.Is(1);
        result.Edges.Count.Is(1);
        result.Edges[0].From.Is(result.RootSymbolId);
        result.Edges[0].Site.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.Edges[0].Site.Line.Is(56);
    }

    [Fact]
    public async Task Run_WithReflectionHeavyMethod_FiltersFrameworkOnlyNoiseByDefault()
    {
        var result = await Sut.Run(CancellationToken.None, path: AppOrchestratorPath, line: 34, column: 41, direction: "downstream", depth: 1);

        result.Error.IsNull();
        result.Root.IsNotNull();
        result.Root!.Name.Is("RunReflectionPathAsync");
        result.Edges.Count.Is(0);
        result.Transitions.IsNull();
        result.RootUncertaintyCategories.IsNotNull();
        result.RootUncertaintyCategories!.Any(static uncertainty => uncertainty == "reflection_blindspot").IsTrue();
        result.PossibleTargetEdges.IsNull();
    }

    [Fact]
    public async Task Run_WithPossibleTargetsMode_ReturnsExplicitPossibleTargetEdges()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, 54, 35);
        var result = await Sut.Run(CancellationToken.None, symbolId: executeFlowAsync.SymbolId, direction: "downstream", depth: 1, includePossibleTargets: true);

        result.Error.IsNull();
        result.Root.IsNotNull();
        result.Edges.Count.Is(1);
        result.PossibleTargetEdges.IsNotNull();

        var possibleTargetEdges = result.PossibleTargetEdges!;
        (possibleTargetEdges.Count >= 2).IsTrue();
        possibleTargetEdges.All(static edge => edge.Kind == "possible_target").IsTrue();
        possibleTargetEdges.All(edge => edge.From == executeFlowAsync.SymbolId).IsTrue();
        possibleTargetEdges.Any(edge => result.Symbols![edge.To].Display == "ProjectImpl.FastWorkItemOperation.ExecuteAsync(ProjectCore.WorkItem, System.Threading.CancellationToken)").IsTrue();
        possibleTargetEdges.Any(edge => result.Symbols![edge.To].Display == "ProjectImpl.SafeWorkItemOperation.ExecuteAsync(ProjectCore.WorkItem, System.Threading.CancellationToken)").IsTrue();

        var directEdge = result.Edges[0];
        directEdge.Kind.Is("direct_static");
        result.Symbols![directEdge.To].Display.Is("ProjectCore.IOperation<ProjectCore.WorkItem, ProjectCore.OperationResult>.ExecuteAsync(ProjectCore.WorkItem, System.Threading.CancellationToken)");
        directEdge.UncertaintyCategories.IsNotNull();
        directEdge.UncertaintyCategories!.Any(static uncertainty => uncertainty == "interface_dispatch").IsTrue();
    }

    [Fact]
    public async Task Run_WithGeneratedRootSymbol_FiltersGeneratedEdgesByDefault()
    {
        var generatedPath = Path.Combine(TestSolutionDirectory, "ProjectApp", "obj", "Debug", "net10.0", "GeneratedExecutionHooks.g.cs");
        var result = await Sut.Run(CancellationToken.None, path: generatedPath, line: 8, column: 24, direction: "downstream", depth: 1);

        result.Error.IsNull();
        result.Root.IsNotNull();
        result.Root!.Name.Is("BeforeRun");
        result.Edges.Count.Is(0);
        result.Transitions.IsNull();
    }

    [Fact]
    public async Task Run_WithInvalidDirection_ReturnsValidationError()
    {
        var result = await Sut.Run(CancellationToken.None, symbolId: "symbol-id", direction: "sideways");
        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_input");
    }

    [Fact]
    public async Task Run_WithUnresolvedSymbolId_ReturnsSymbolNotFound()
    {
        var result = await Sut.Run(CancellationToken.None, symbolId: "ProjectApp:DoesNotExist", direction: "downstream");
        result.Error.IsNotNull();
        result.Error!.Code.Is("symbol_not_found");
        result.Root.IsNull();
        result.Edges.IsEmpty();
    }

    [Fact]
    public async Task Run_WithoutSelector_ReturnsValidationError()
    {
        var result = await Sut.Run(CancellationToken.None, direction: "downstream");
        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_input");
        result.Root.IsNull();
        result.Edges.IsEmpty();
    }

    [Fact]
    public async Task Run_DefaultSerialization_OmitsLegacyEdgeBallast()
    {
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, 15, 44);
        var result = await Sut.Run(CancellationToken.None, symbolId: runAsync.SymbolId, direction: "downstream", depth: 2);

        result.Error.IsNull();
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        json.Contains("fromReference", StringComparison.Ordinal).IsFalse();
        json.Contains("toReference", StringComparison.Ordinal).IsFalse();
        json.Contains("possibleTargets", StringComparison.Ordinal).IsFalse();
        json.Contains("\"rootSymbol\":", StringComparison.Ordinal).IsFalse();
        json.Contains("possibleTargetEdges", StringComparison.Ordinal).IsFalse();
        (json.Length < 7000).IsTrue();
    }

    private async Task<RoslynMcp.Tools.Inspection.ResolveSymbol.ResolvedSymbol> ResolveSymbolAsync(string path, int line, int column)
    {
        var resolver = Context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var result = await resolver.Run(CancellationToken.None, path: path, line: line, column: column);
        result.Error.IsNull();
        result.Symbol.IsNotNull();
        return result.Symbol!;
    }
}

public sealed class TraceCallFlowToolIsolatedTests(ITestOutputHelper output)
    : IsolatedToolTests<RoslynMcp.Tools.Inspection.TraceCallFlow.Tool>(output)
{
    [Fact]
    public async Task Run_ExcludesTestFileCallersFromDefaultResults()
    {
        await using var context = await CreateContextAsync();
        var traceTool = GetSut(context);
        var resolveTool = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var loadSolution = context.GetRequiredService<RoslynMcp.Tools.Inspection.LoadSolution.Tool>();
        var testFilePath = Path.Combine(context.TestSolutionDirectory, "ProjectApp", "RunAsyncTests.cs");

        await File.WriteAllTextAsync(testFilePath, """
using ProjectCore;
using ProjectImpl;

namespace ProjectApp;

public static class RunAsyncTests
{
    public static Task<OperationResult> ExecuteAsync(CancellationToken cancellationToken = default)
        => new AppOrchestrator(new FastWorkItemOperation()).RunAsync(cancellationToken);
}
""");

        var load = await loadSolution.Run(CancellationToken.None, context.SolutionPath);
        load.Error.IsNull();

        var runAsync = await resolveTool.Run(CancellationToken.None, path: Path.Combine(context.TestSolutionDirectory, "ProjectApp", "AppOrchestrator.cs"), line: 15, column: 44);
        runAsync.Error.IsNull();
        runAsync.Symbol.IsNotNull();

        var result = await traceTool.Run(CancellationToken.None, symbolId: runAsync.Symbol!.SymbolId, direction: "upstream", depth: 1);
        result.Error.IsNull();
        result.Edges.Count.Is(2);
        result.Edges.Any(edge => edge.Site.FilePath.HasPathSuffix(Path.Combine("ProjectApp", "RunAsyncTests.cs"))).IsFalse();
    }

    [Fact]
    public async Task Run_WithDynamicDispatchRoot_ReportsDynamicUnresolvedBlindspot()
    {
        await using var context = await CreateContextAsync();
        var traceTool = GetSut(context);
        var resolveTool = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var loadSolution = context.GetRequiredService<RoslynMcp.Tools.Inspection.LoadSolution.Tool>();
        var dynamicPath = Path.Combine(context.TestSolutionDirectory, "ProjectApp", "DynamicDispatchProbe.cs");

        await File.WriteAllTextAsync(dynamicPath, """
namespace ProjectApp;

public static class DynamicDispatchProbe
{
    public static string RunDynamic(object value)
    {
        dynamic candidate = value;
        return candidate.ToString();
    }
}
""");

        var load = await loadSolution.Run(CancellationToken.None, context.SolutionPath);
        load.Error.IsNull();

        var root = await resolveTool.Run(CancellationToken.None, path: dynamicPath, line: 5, column: 26);
        root.Error.IsNull();
        root.Symbol.IsNotNull();

        var result = await traceTool.Run(CancellationToken.None, symbolId: root.Symbol!.SymbolId, direction: "downstream", depth: 1);
        result.Error.IsNull();
        result.Root.IsNotNull();
        result.RootUncertaintyCategories.IsNotNull();
        result.RootUncertaintyCategories!.Any(static uncertainty => uncertainty == "dynamic_unresolved").IsTrue();
    }
}

file static class TraceAssertions
{
    internal static void AssertEdge(this IReadOnlyList<RoslynMcp.Tools.Inspection.TraceCallFlow.TraceFlowEdge> edges, string fromSymbolId, string toSymbolId, string expectedFileSuffix, int expectedLine)
    {
        edges.Any(edge => edge.From == fromSymbolId && edge.To == toSymbolId && edge.Site.FilePath.HasPathSuffix(expectedFileSuffix) && edge.Site.Line == expectedLine).IsTrue();
    }

    internal static RoslynMcp.Tools.Inspection.TraceCallFlow.TraceFlowEdge GetEdge(this IReadOnlyList<RoslynMcp.Tools.Inspection.TraceCallFlow.TraceFlowEdge> edges, string fromSymbolId, string toSymbolId, string expectedFileSuffix, int expectedLine)
        => edges.Single(edge => edge.From == fromSymbolId && edge.To == toSymbolId && edge.Site.FilePath.HasPathSuffix(expectedFileSuffix) && edge.Site.Line == expectedLine);

    internal static void AssertTransition(this IReadOnlyList<RoslynMcp.Tools.Inspection.TraceCallFlow.FlowTransition> transitions, string fromProject, string toProject, int expectedCount)
    {
        transitions.Any(transition => transition.FromProject == fromProject && transition.ToProject == toProject && transition.Count == expectedCount).IsTrue();
    }
}
