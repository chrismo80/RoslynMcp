using Is.Assertions;
using RoslynMcp.Tools.Inspection.ResolveSymbol;
using RoslynMcp.Tools.Inspection.TraceCallFlow;
using RoslynMcp.Tools.Tests.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

public sealed class FindCallersToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<RoslynMcp.Tools.Inspection.FindCallers.Tool>(fixture, output), IClassFixture<SharedSandboxFixture>
{
    [Fact]
    public async Task Run_WithExecuteFlowAsyncSymbol_ReturnsImmediateUpstreamCallersOnly()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, 54, 35);
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, 15, 44);
        var runFastAsync = await ResolveSymbolAsync(AppOrchestratorPath, 78, 41);
        var runSafeAsync = await ResolveSymbolAsync(AppOrchestratorPath, 83, 41);

        var result = await Sut.Run(CancellationToken.None, executeFlowAsync.SymbolId);

        result.Error.IsNull();
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
        edge.Kind.Is("direct_static");
        edge.UncertaintyCategories.IsNull();
        result.PossibleTargetEdges.IsNull();

        result.Edges.Any(candidate => candidate.From == runFastAsync.SymbolId).IsFalse();
        result.Edges.Any(candidate => candidate.From == runSafeAsync.SymbolId).IsFalse();
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

    private async Task<ResolvedSymbol> ResolveSymbolAsync(string path, int line, int column)
    {
        var resolver = Context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var result = await resolver.Run(CancellationToken.None, path: path, line: line, column: column);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        return result.Symbol!;
    }
}
