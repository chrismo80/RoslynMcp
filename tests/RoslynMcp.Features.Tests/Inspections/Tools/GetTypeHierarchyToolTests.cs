using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class GetTypeHierarchyToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<GetTypeHierarchyTool>(fixture, output)
{
    [Fact]
    public async Task GetTypeHierarchyAsync_WithClassSymbol_ReturnsTransitiveHierarchy()
    {
        var symbolId = await ResolveSymbolIdAsync(HierarchyPath, line: 23, column: 18);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("DerivedClass");
        result.Symbol.Kind.Is("NamedType");
        result.Symbol.Location.IsNotNull();
        result.Symbol.Location!.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectCore", "Hierarchy.cs"));
        result.Symbol.Location.Line.Is(23);

        result.BaseTypes.Select(static type => type.Display).ToArray().Is(new[] { "BaseClass", "Object" });

        result.ImplementedInterfaces.ShouldMatchSymbols(("IWorker", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 3, null));
        result.DerivedTypes.ShouldMatchSymbols(("LeafClass", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 28, null));
    }

    [Fact]
    public async Task GetTypeHierarchyAsync_WithClassSymbolAndIncludeTransitiveFalse_ReturnsImmediateHierarchyOnly()
    {
        var symbolId = await ResolveSymbolIdAsync(HierarchyPath, line: 23, column: 18);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, includeTransitive: false);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("DerivedClass");

        result.BaseTypes.Select(static type => type.Display).Is("BaseClass");
        result.ImplementedInterfaces.IsEmpty();

        result.DerivedTypes.ShouldMatchSymbols(("LeafClass", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 28, null));
    }

    [Fact]
    public async Task GetTypeHierarchyAsync_WithInterfaceSymbol_ReturnsDerivedInterfacesAndImplementations()
    {
        var symbolId = await ResolveSymbolIdAsync(HierarchyPath, line: 3, column: 18);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("IWorker");
        result.Symbol.Kind.Is("NamedType");
        result.BaseTypes.IsEmpty();
        result.ImplementedInterfaces.IsEmpty();

        result.DerivedTypes.ShouldMatchSymbols(
            ("BaseClass", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 18, null),
            ("DerivedClass", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 23, null),
            ("LeafClass", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 28, null),
            ("RoundRobinWorker", "NamedType", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 5, null),
            ("WorkerA", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 8, null),
            ("WorkerB", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 13, null));
    }

    [Fact]
    public async Task GetTypeHierarchyAsync_WithMaxDerivedLimit_ReturnsLimitedDerivedTypes()
    {
        var symbolId = await ResolveSymbolIdAsync(HierarchyPath, line: 18, column: 18);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, maxDerived: 1);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("BaseClass");
        result.DerivedTypes.Count.Is(1);
        result.DerivedTypes.ShouldMatchSymbols(("DerivedClass", "NamedType", Path.Combine("ProjectCore", "Hierarchy.cs"), 23, null));
    }

    [Fact]
    public async Task GetTypeHierarchyAsync_WithUnresolvedSymbolId_ReturnsSymbolNotFound()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, "not-a-real-symbol-id");

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.Symbol.IsNull();
        result.BaseTypes.IsEmpty();
        result.ImplementedInterfaces.IsEmpty();
        result.DerivedTypes.IsEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetTypeHierarchyAsync_WithInvalidSymbolId_ReturnsValidationError(string symbolId)
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
        result.Symbol.IsNull();
        result.BaseTypes.IsEmpty();
        result.ImplementedInterfaces.IsEmpty();
        result.DerivedTypes.IsEmpty();
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
        internal void ShouldMatchSymbols(params (string Name, string Kind, string FileName, int Line, string? ContainingType)[] expected)
        {
            actual.Count.Is(expected.Length);

            for (var i = 0; i < expected.Length; i++)
            {
                actual[i].Display.Is(expected[i].Name);
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
