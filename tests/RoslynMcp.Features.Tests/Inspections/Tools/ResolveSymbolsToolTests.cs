using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class ResolveSymbolsToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<ResolveSymbolsTool>(fixture, output)
{
    [Fact]
    public async Task ResolveSymbolsAsync_WithMixedSelectors_ReturnsPerItemResults()
    {
        var result = await Sut.ExecuteAsync(
            CancellationToken.None,
            [
                new ResolveSymbolBatchEntry(QualifiedName: "ProjectApp.AppOrchestrator", Label: "type"),
                new ResolveSymbolBatchEntry(Path: AppOrchestratorPath, Line: 54, Column: 35, Label: "method"),
                new ResolveSymbolBatchEntry(QualifiedName: "ProjectCore.OperationBase<TInput>", ProjectName: "ProjectCore", Label: "generic")
            ]);

        result.Error.ShouldBeNone();
        result.TotalCount.Is(3);
        result.ResolvedCount.Is(3);
        result.AmbiguousCount.Is(0);
        result.ErrorCount.Is(0);

        result.Results.Count.Is(3);
        result.Results[0].Index.Is(0);
        result.Results[0].Label.Is("type");
        result.Results[0].Symbol.IsNotNull();
        var firstSymbol = result.Results[0].Symbol!;
        firstSymbol.QualifiedDisplayName.IsNull();

        result.Results[1].Index.Is(1);
        result.Results[1].Label.Is("method");
        result.Results[1].Symbol.IsNotNull();
        result.Results[1].Symbol!.DisplayName.Contains("ExecuteFlowAsync", StringComparison.Ordinal).IsTrue();

        result.Results[2].Index.Is(2);
        result.Results[2].Label.Is("generic");
        result.Results[2].Symbol.IsNotNull();
        result.Results[2].Symbol!.QualifiedDisplayName.IsNull();
    }

    [Fact]
    public async Task ResolveSymbolsAsync_WithAmbiguousAndInvalidEntries_AggregatesItemOutcomes()
    {
        var result = await Sut.ExecuteAsync(
            CancellationToken.None,
            [
                new ResolveSymbolBatchEntry(QualifiedName: "ProjectImpl.FastWorkItemOperation.ExecuteAsync", ProjectName: "ProjectImpl", Label: "ambiguous"),
                new ResolveSymbolBatchEntry(QualifiedName: "ProjectApp.DoesNotExist", Label: "missing")
            ]);

        result.Error.ShouldBeNone();
        result.TotalCount.Is(2);
        result.ResolvedCount.Is(0);
        result.AmbiguousCount.Is(1);
        result.ErrorCount.Is(2);

        var ambiguous = result.Results[0];
        ambiguous.Label.Is("ambiguous");
        ambiguous.Symbol.IsNull();
        ambiguous.IsAmbiguous.IsTrue();
        ambiguous.Error.ShouldHaveCode(ErrorCodes.AmbiguousSymbol);
        ambiguous.Candidates.Count.Is(3);
        ambiguous.Candidates.All(static candidate => candidate.QualifiedDisplayName is not null).IsTrue();

        var missing = result.Results[1];
        missing.Label.Is("missing");
        missing.Symbol.IsNull();
        missing.IsAmbiguous.IsFalse();
        missing.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
    }

    [Fact]
    public async Task ResolveSymbolsAsync_WithEmptyEntries_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, Array.Empty<ResolveSymbolBatchEntry>());

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
        result.Results.IsEmpty();
        result.TotalCount.Is(0);
    }
}
