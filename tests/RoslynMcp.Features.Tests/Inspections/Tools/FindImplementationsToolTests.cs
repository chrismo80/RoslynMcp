using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
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
        result.Symbol!.Name.Is("IWorker");
        result.Symbol.Kind.Is("NamedType");
        result.Symbol.DeclarationLocation.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectCore", "Hierarchy.cs"));
        result.Symbol.DeclarationLocation.Line.Is(3);

        ShouldMatchImplementations(result.Implementations,
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
        result.Symbol!.Name.Is("Work");
        result.Symbol.Kind.Is("Method");
        result.Symbol.ContainingType.Is("global::ProjectCore.IWorker");

        ShouldMatchImplementations(result.Implementations,
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
        result.Symbol!.Name.Is("ExecuteAsync");
        result.Symbol.Kind.Is("Method");
        result.Symbol.ContainingType.Is("global::ProjectCore.OperationBase<TInput>");
        result.Implementations.IsEmpty();
    }

    [Fact]
    public async Task FindImplementationsAsync_WithVirtualMethodWithoutOverrides_ReturnsEmptyResult()
    {
        var symbolId = await ResolveSymbolIdAsync(ContractsPath, line: 49, column: 30);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Name.Is("DelayAsync");
        result.Symbol.Kind.Is("Method");
        result.Symbol.ContainingType.Is("global::ProjectCore.OperationBase<TInput>");
        result.Implementations.IsEmpty();
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

    private static void ShouldMatchImplementations(
        IReadOnlyList<SymbolDescriptor> actual,
        params (string Name, string Kind, string FileName, int Line, string? ContainingType)[] expected)
    {
        actual.Count.Is(expected.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            actual[i].Name.Is(expected[i].Name);
            actual[i].Kind.Is(expected[i].Kind);
            actual[i].ContainingType.Is(expected[i].ContainingType);
            actual[i].DeclarationLocation.FilePath.ShouldEndWithPathSuffix(expected[i].FileName);
            actual[i].DeclarationLocation.Line.Is(expected[i].Line);
        }
    }
}