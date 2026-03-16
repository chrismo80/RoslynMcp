using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class FindImplementationsToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<FindImplementationsTool>(fixture, output)
{

    [Fact]
    public async Task FindImplementationsAsync_WithInterfaceSymbol_ReturnsOrderedImplementations()
    {
        var symbolId = await ResolveSymbolIdAsync(HierarchyPath, line: 3, column: 18);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("IWorker");
        result.Symbol.Kind.Is("NamedType");
        result.Symbol.Location.IsNotNull();
        result.Symbol.Location!.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectCore", "Hierarchy.cs"));
        result.Symbol.Location.Line.Is(3);

        result.Implementations.ShouldMatchImplementations(
            ("BaseClass", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 18, null),
            ("DerivedClass", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 23, null),
            ("LeafClass", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 28, null),
            ("RoundRobinWorker", "NamedType", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 5, null),
            ("WorkerA", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 8, null),
            ("WorkerB", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 13, null));
    }

    [Fact]
    public async Task FindImplementationsAsync_WithInterfaceMethodSymbol_ReturnsDirectImplementingMethods()
    {
        var symbolId = await ResolveSymbolIdAsync(HierarchyPath, line: 5, column: 10);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Contains("Work", StringComparison.Ordinal).IsTrue();
        result.Symbol.Kind.Is("Method");
        result.Symbol.Owner.Is("global::ProjectCore.IWorker");

        result.Implementations.ShouldMatchImplementations(
            ("Work", "Method", Path.Combine("ProjectCore", "Hierarchy.cs"), 20, "global::ProjectCore.BaseClass"),
            ("Work", "Method", Path.Combine("ProjectCore", "Hierarchy.cs"), 10, "global::ProjectCore.WorkerA"),
            ("Work", "Method", Path.Combine("ProjectCore", "Hierarchy.cs"), 15, "global::ProjectCore.WorkerB"),
            ("Work", "Method", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 9, "global::ProjectImpl.RoundRobinWorker"));
    }

    [Fact]
    public async Task FindImplementationsAsync_WithAbstractMethodSymbol_ReturnsEmptyResult()
    {
        var symbolId = await ResolveSymbolIdAsync(ContractsPath, line: 41, column: 45);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Contains("ExecuteAsync", StringComparison.Ordinal).IsTrue();
        result.Symbol.Kind.Is("Method");
        result.Symbol.Owner.Is("global::ProjectCore.OperationBase<TInput>");
        result.Implementations.ShouldMatchImplementations(
            ("ExecuteAsync", "Method", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 17, "global::ProjectImpl.FastWorkItemOperation"),
            ("ExecuteAsync", "Method", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 40, "global::ProjectImpl.SafeWorkItemOperation"));
    }

    [Fact]
    public async Task FindImplementationsAsync_WithVirtualMethod_ReturnsOverrides()
    {
        var symbolId = await ResolveSymbolIdAsync(ContractsPath, line: 49, column: 30);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Contains("DelayAsync", StringComparison.Ordinal).IsTrue();
        result.Symbol.Kind.Is("Method");
        result.Symbol.Owner.Is("global::ProjectCore.OperationBase<TInput>");
        result.Implementations.ShouldMatchImplementations(("DelayAsync", "Method", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 48, "global::ProjectImpl.SafeWorkItemOperation"));
    }

    [Fact]
    public async Task FindImplementationsAsync_WithInterfaceMember_MatchesTraceCallFlowPossibleTargets()
    {
        var interfaceMethodSymbolId = await ResolveSymbolIdAsync(AppOrchestratorPath, line: 56, column: 27);
        var appOrchestratorExecuteFlowAsync = await ResolveSymbolIdAsync(AppOrchestratorPath, line: 54, column: 35);
        var traceTool = Context.GetRequiredService<TraceCallFlowTool>();

        var implementations = await Sut.ExecuteAsync(CancellationToken.None, interfaceMethodSymbolId);
        var trace = await traceTool.ExecuteAsync(CancellationToken.None, symbolId: appOrchestratorExecuteFlowAsync, direction: "downstream", depth: 1, includePossibleTargets: true);

        implementations.Error.ShouldBeNone();
        trace.Error.ShouldBeNone();

        var dispatchEdge = trace.Edges.Single(edge => edge.UncertaintyCategories is not null && edge.UncertaintyCategories.Contains(FlowUncertaintyCategories.InterfaceDispatch, StringComparer.Ordinal));
        trace.PossibleTargetEdges.IsNotNull();

        var implementationIds = implementations.Implementations
            .Select(static implementation => implementation.SymbolId)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        var possibleTargetIds = trace.PossibleTargetEdges!
            .Select(static edge => edge.To)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        implementationIds.Is(possibleTargetIds);
    }

    [Fact]
    public async Task FindImplementationsAsync_WithUnresolvedSymbolId_ReturnsSymbolNotFound()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, "not-a-real-symbol-id");

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.Symbol.IsNull();
        result.Implementations.IsEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindImplementationsAsync_WithInvalidSymbolId_ReturnsValidationError(string symbolId)
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
        result.Symbol.IsNull();
        result.Implementations.IsEmpty();
    }

    private async Task<string> ResolveSymbolIdAsync(string path, int line, int column)
    {
        var resolver = Context.GetRequiredService<ResolveSymbolTool>();
        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: path, line: line, column: column);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();

        return resolved.Symbol!.SymbolId;
    }
}

file static class AssertionExtensions
{
    extension(IReadOnlyList<CompactSymbolSummary> actual)
    {
        internal void ShouldMatchImplementations(params (string Name, string Kind, string FileName, int Line, string? ContainingType)[] expected)
        {
            actual.Count.Is(expected.Length);

            for (var i = 0; i < expected.Length; i++)
            {
                actual[i].Display.Contains(expected[i].Name, StringComparison.Ordinal).IsTrue();
                actual[i].Kind.Is(expected[i].Kind);
                if (expected[i].ContainingType != null)
                    actual[i].Owner.Is(expected[i].ContainingType);
                actual[i].Location.IsNotNull();
                actual[i].Location!.FilePath.ShouldEndWithPathSuffix(expected[i].FileName);
                actual[i].Location.Line.Is(expected[i].Line);
            }
        }
    }
}
