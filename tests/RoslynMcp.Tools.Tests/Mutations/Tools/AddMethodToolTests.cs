using Is.Assertions;
using RoslynMcp.Tools.Inspection.ResolveSymbol;
using RoslynMcp.Tools.Tests.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Mutations.Tools;

public sealed class AddMethodToolTests(ITestOutputHelper output)
    : IsolatedToolTests<RoslynMcp.Tools.Mutation.AddMethod.Tool>(output)
{
    [Fact]
    public async Task Run_WithValidMethod_AddsMethodAndReturnsCreatedSymbol()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var targetTypeSymbolId = await ResolveMethodMutationTestTargetAsync(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");

        var result = await sut.Run(
            CancellationToken.None,
            targetTypeSymbolId,
            "Plan",
            "string",
            "public",
            [],
            ["string input", "int priority", "bool isEnabled"],
            "var plan = string.Empty;\\r\\nreturn plan;");

        result.Error.IsNull();
        result.Status.Is("applied");
        result.ChangedFiles.Count.Is(1);
        result.ChangedFiles[0].ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"));
        result.TargetTypeSymbolId.Is(targetTypeSymbolId);
        result.AddedMethod.IsNotNull();
        result.AddedMethod!.SymbolId.ShouldNotBeEmpty();
        result.AddedMethod.Signature.Contains("Plan", StringComparison.Ordinal).IsTrue();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Plan(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
        text.Contains("var plan = string.Empty;", StringComparison.Ordinal).IsTrue();
        text.Contains("return plan;", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task Run_WithUnknownTargetSymbolId_ReturnsSymbolNotFoundWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.Run(
            CancellationToken.None,
            "not-a-real-symbol-id",
            "Plan",
            "string",
            "public",
            [],
            ["string input"],
            "return string.Empty;");

        result.Error.IsNotNull();
        result.Error!.Code.Is("symbol_not_found");
        result.Status.Is("failed");
        result.AddedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    private static async Task<string> ResolveMethodMutationTestTargetAsync(IsolatedSandboxContext context)
    {
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var resolved = await resolver.Run(
            CancellationToken.None,
            qualifiedName: "ProjectApp.MethodMutationTestTarget",
            projectName: "ProjectApp");

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();
        return resolved.Symbol!.SymbolId;
    }
}
