using Is.Assertions;
using RoslynMcp.Tools.Tests.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

public sealed class FindImplementationsToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<RoslynMcp.Tools.Inspection.FindImplementations.Tool>(fixture, output), IClassFixture<SharedSandboxFixture>
{
    [Fact]
    public async Task Run_WithInterfaceSymbol_ReturnsOrderedImplementations()
    {
        var symbolId = await ResolveSymbolIdAsync(HierarchyPath, line: 3, column: 18);

        var result = await Sut.Run(CancellationToken.None, symbolId);

        result.Error.IsNull();
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
    public async Task Run_WithInterfaceMethodSymbol_ReturnsDirectImplementingMethods()
    {
        var symbolId = await ResolveSymbolIdAsync(HierarchyPath, line: 5, column: 10);

        var result = await Sut.Run(CancellationToken.None, symbolId);

        result.Error.IsNull();
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
    public async Task Run_WithAbstractMethodSymbol_ReturnsEmptyResult()
    {
        var symbolId = await ResolveSymbolIdAsync(ContractsPath, line: 41, column: 45);

        var result = await Sut.Run(CancellationToken.None, symbolId);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Contains("ExecuteAsync", StringComparison.Ordinal).IsTrue();
        result.Symbol.Kind.Is("Method");
        result.Symbol.Owner.Is("global::ProjectCore.OperationBase<TInput>");
        result.Implementations.ShouldMatchImplementations(
            ("ExecuteAsync", "Method", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 17, "global::ProjectImpl.FastWorkItemOperation"),
            ("ExecuteAsync", "Method", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 40, "global::ProjectImpl.SafeWorkItemOperation"));
    }

    [Fact]
    public async Task Run_WithVirtualMethod_ReturnsOverrides()
    {
        var symbolId = await ResolveSymbolIdAsync(ContractsPath, line: 49, column: 30);

        var result = await Sut.Run(CancellationToken.None, symbolId);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Contains("DelayAsync", StringComparison.Ordinal).IsTrue();
        result.Symbol.Kind.Is("Method");
        result.Symbol.Owner.Is("global::ProjectCore.OperationBase<TInput>");
        result.Implementations.ShouldMatchImplementations(("DelayAsync", "Method", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 48, "global::ProjectImpl.SafeWorkItemOperation"));
    }

    [Fact]
    public async Task Run_WithInterfaceMember_MatchesTraceCallFlowPossibleTargets()
    {
        var interfaceMethodSymbolId = await ResolveSymbolIdAsync(AppOrchestratorPath, line: 56, column: 27);
        var appOrchestratorExecuteFlowAsync = await ResolveSymbolIdAsync(AppOrchestratorPath, line: 54, column: 35);
        var traceTool = Context.GetRequiredService<RoslynMcp.Tools.Inspection.TraceCallFlow.Tool>();

        var implementations = await Sut.Run(CancellationToken.None, interfaceMethodSymbolId);
        var trace = await traceTool.Run(CancellationToken.None, symbolId: appOrchestratorExecuteFlowAsync, direction: "downstream", depth: 1, includePossibleTargets: true);

        implementations.Error.IsNull();
        trace.Error.IsNull();

        var dispatchEdge = trace.Edges.Single(edge => edge.UncertaintyCategories is not null && edge.UncertaintyCategories.Contains("interface_dispatch", StringComparer.Ordinal));
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
    public async Task Run_WithUnresolvedSymbolId_ReturnsSymbolNotFound()
    {
        var result = await Sut.Run(CancellationToken.None, "not-a-real-symbol-id");

        result.Error.IsNotNull();
        result.Error!.Code.Is("symbol_not_found");
        result.Symbol.IsNull();
        result.Implementations.IsEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Run_WithInvalidSymbolId_ReturnsValidationError(string symbolId)
    {
        var result = await Sut.Run(CancellationToken.None, symbolId);

        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_input");
        result.Symbol.IsNull();
        result.Implementations.IsEmpty();
    }

    private async Task<string> ResolveSymbolIdAsync(string path, int line, int column)
    {
        var resolver = Context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var resolved = await resolver.Run(CancellationToken.None, path: path, line: line, column: column);

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();

        return resolved.Symbol!.SymbolId;
    }
}

file static class AssertionExtensions
{
    internal static void ShouldMatchImplementations(this IReadOnlyList<RoslynMcp.Tools.Inspection.FindImplementations.CompactSymbol> actual, params (string Name, string Kind, string FileName, int Line, string? ContainingType)[] expected)
    {
        actual.Count.Is(expected.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            actual[i].Display.Contains(expected[i].Name, StringComparison.Ordinal).IsTrue();
            actual[i].Kind.Is(expected[i].Kind);
            if (expected[i].ContainingType is not null)
                actual[i].Owner.Is(expected[i].ContainingType);
            actual[i].Location.IsNotNull();
            actual[i].Location!.FilePath.ShouldEndWithPathSuffix(expected[i].FileName);
            actual[i].Location.Line.Is(expected[i].Line);
        }
    }
}
