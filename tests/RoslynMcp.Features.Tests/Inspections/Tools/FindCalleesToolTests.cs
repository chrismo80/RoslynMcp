using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class FindCalleesToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<FindCalleesTool>(fixture, output)
{
    [Fact]
    public async Task FindCalleesAsync_WithRunAsyncSymbol_ReturnsImmediateDownstreamCalleesOnly()
    {
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var startAsync = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), line: 5, column: 23);
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 54, column: 35);
        var calculate = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "CodeSmells"), line: 23, column: 16);
        var stop = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), line: 12, column: 17);
        var changeState = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.State"), line: 11, column: 18);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: runAsync.SymbolId);

        result.Error.ShouldBeNone();
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
        directEdge.Kind.Is(FlowEvidenceKinds.DirectStatic);
        directEdge.UncertaintyCategories.IsNull();
        result.PossibleTargetEdges.IsNull();

        result.Edges.Any(candidate => candidate.To == changeState.SymbolId).IsFalse();
    }

    [Fact]
    public async Task FindCalleesAsync_WithoutSelector_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
        result.Root.IsNull();
        result.Edges.IsEmpty();
    }

    [Fact]
    public async Task FindCalleesAsync_WithExecuteFlowAsyncSymbol_ReturnsDownstreamDirectionAndDepthOne()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 54, column: 35);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.SymbolId);

        result.Error.ShouldBeNone();
        result.Direction.Is("downstream");
        result.Depth.Is(1);
    }

    private async Task<ResolvedSymbolSummary> ResolveSymbolAsync(string path, int line, int column)
    {
        var resolver = Context.GetRequiredService<ResolveSymbolTool>();
        var result = await resolver.ExecuteAsync(CancellationToken.None, path: path, line: line, column: column);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        return result.Symbol!;
    }
}

file static class AssertionExtensions
{
    extension(IReadOnlyList<TraceFlowEdge> edges)
    {
        internal void AssertEdge(string fromSymbolId, string toSymbolId, string expectedFileSuffix, int expectedLine)
        {
            edges.Any(edge =>
                edge.From == fromSymbolId &&
                edge.To == toSymbolId &&
                edge.Site.FilePath.HasPathSuffix(expectedFileSuffix) &&
                edge.Site.Line == expectedLine).IsTrue();
        }

        internal TraceFlowEdge GetEdge(string fromSymbolId, string toSymbolId, string expectedFileSuffix, int expectedLine)
            => edges.Single(edge =>
                edge.From == fromSymbolId &&
                edge.To == toSymbolId &&
                edge.Site.FilePath.HasPathSuffix(expectedFileSuffix) &&
                edge.Site.Line == expectedLine);
    }
}
