using Is.Assertions;
using RoslynMcp.Tools.Inspection.ResolveSymbol;
using RoslynMcp.Tools.Inspection.TraceCallFlow;
using RoslynMcp.Tools.Tests.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

public sealed class FindCalleesToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<RoslynMcp.Tools.Inspection.FindCallees.Tool>(fixture, output), IClassFixture<SharedSandboxFixture>
{
    [Fact]
    public async Task Run_WithRunAsyncSymbol_ReturnsImmediateDownstreamCalleesOnly()
    {
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, 15, 44);
        var startAsync = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), 5, 23);
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, 54, 35);
        var calculate = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "CodeSmells"), 23, 16);
        var stop = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), 12, 17);
        var changeState = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.State"), 11, 18);

        var result = await Sut.Run(CancellationToken.None, runAsync.SymbolId);

        result.Error.IsNull();
        result.Root.IsNotNull();
        result.Root!.Name.Is("RunAsync");
        result.Direction.Is("downstream");
        result.Depth.Is(1);
        result.Edges.Count.Is(5);
        result.Edges.All(candidate => candidate.From == runAsync.SymbolId).IsTrue();

        result.Edges.AssertEdge(runAsync.SymbolId, startAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 20);
        result.Edges.AssertEdge(runAsync.SymbolId, executeFlowAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);
        result.Edges.AssertEdge(runAsync.SymbolId, calculate.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 25);
        result.Edges.AssertEdge(runAsync.SymbolId, stop.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 27);

        var directEdge = result.Edges.GetEdge(runAsync.SymbolId, startAsync.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 20);
        directEdge.Kind.Is("direct_static");
        directEdge.UncertaintyCategories.IsNull();
        result.PossibleTargetEdges.IsNull();

        result.Edges.Any(candidate => candidate.To == changeState.SymbolId).IsFalse();
    }

    [Fact]
    public async Task Run_WithoutSelector_ReturnsValidationError()
    {
        var result = await Sut.Run(CancellationToken.None);

        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_input");
        result.Root.IsNull();
        result.Edges.IsEmpty();
    }

    [Fact]
    public async Task Run_WithExecuteFlowAsyncSymbol_ReturnsDownstreamDirectionAndDepthOne()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, 54, 35);

        var result = await Sut.Run(CancellationToken.None, executeFlowAsync.SymbolId);

        result.Error.IsNull();
        result.Direction.Is("downstream");
        result.Depth.Is(1);
    }

    private async Task<ResolvedSymbol> ResolveSymbolAsync(string path, int line, int column)
    {
        var resolver = Context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var result = await resolver.Run(CancellationToken.None, path: path, line: line, column: column);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        return result.Symbol!;
    }
}

file static class FindCalleesAssertionExtensions
{
    internal static void AssertEdge(this IReadOnlyList<TraceFlowEdge> edges, string fromSymbolId, string toSymbolId, string expectedFileSuffix, int expectedLine)
    {
        edges.Any(edge =>
            edge.From == fromSymbolId &&
            edge.To == toSymbolId &&
            edge.Site.FilePath.HasPathSuffix(expectedFileSuffix) &&
            edge.Site.Line == expectedLine).IsTrue();
    }

    internal static TraceFlowEdge GetEdge(this IReadOnlyList<TraceFlowEdge> edges, string fromSymbolId, string toSymbolId, string expectedFileSuffix, int expectedLine)
        => edges.Single(edge =>
            edge.From == fromSymbolId &&
            edge.To == toSymbolId &&
            edge.Site.FilePath.HasPathSuffix(expectedFileSuffix) &&
            edge.Site.Line == expectedLine);
}
