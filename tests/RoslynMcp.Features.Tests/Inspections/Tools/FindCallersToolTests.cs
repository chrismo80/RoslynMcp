using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class FindCallersToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<FindCallersTool>(fixture, output)
{
    [Fact]
    public async Task FindCallersAsync_WithExecuteFlowAsyncSymbol_ReturnsImmediateUpstreamCallersOnly()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 54, column: 35);
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var runFastAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 78, column: 41);
        var runSafeAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 83, column: 41);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.SymbolId);

        result.Error.ShouldBeNone();
        result.Root.IsNotNull();
        result.Root!.Name.Is("ExecuteFlowAsync");
        result.Direction.Is("upstream");
        result.Depth.Is(1);
        result.Edges.Count.Is(1);

        var edge = result.Edges[0];
        edge.From.Is(runAsync.SymbolId);
        edge.To.Is(executeFlowAsync.SymbolId);
        edge.Site.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        edge.Site.Line.Is(23);
        edge.Kind.Is(FlowEvidenceKinds.DirectStatic);
        edge.UncertaintyCategories.IsNull();
        result.PossibleTargetEdges.IsNull();

        result.Edges.Any(candidate => candidate.From == runFastAsync.SymbolId).IsFalse();
        result.Edges.Any(candidate => candidate.From == runSafeAsync.SymbolId).IsFalse();
    }

    [Fact]
    public async Task FindCallersAsync_WithoutSelector_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
        result.Root.IsNull();
        result.Edges.IsEmpty();
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
