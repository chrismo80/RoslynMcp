using Is.Assertions;
using RoslynMcp.Tools.Inspection.ResolveSymbol;
using RoslynMcp.Tools.Tests.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

[Collection(CurrentDirectorySensitiveCollection.Name)]
public sealed class ResolveSymbolToolTests(ITestOutputHelper output)
    : IsolatedToolTests<Tool>(output)
{
    [Fact]
    public async Task Run_WithQualifiedName_ReturnsResolvedTypeSymbol()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.Run(CancellationToken.None, qualifiedName: "ProjectApp.AppOrchestrator", projectName: "ProjectApp");

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.IsNotNull();
        result.Symbol!.DisplayName.Is("AppOrchestrator");
        result.Symbol.Kind.Is("NamedType");
        result.Symbol.SymbolId.ShouldNotBeEmpty();
        result.Symbol.Location.IsNotNull();
        result.Symbol.Location!.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
    }

    [Fact]
    public async Task Run_WithInvalidQualifiedName_ReturnsSymbolNotFound()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.Run(CancellationToken.None, qualifiedName: "ProjectApp.DoesNotExist", projectName: "ProjectApp");

        result.Error.IsNotNull();
        result.Error!.Code.Is("symbol_not_found");
        result.Symbol.IsNull();
        result.Candidates.IsEmpty();
    }

    [Fact]
    public async Task Run_WithAbsoluteSourcePosition_ReturnsResolvedSymbol()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.Run(CancellationToken.None, path: context.GetFilePath("ProjectApp", "AppOrchestrator"), line: 6, column: 21);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.Symbol!.Location.IsNotNull();
        result.Symbol.Location!.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
    }
}
