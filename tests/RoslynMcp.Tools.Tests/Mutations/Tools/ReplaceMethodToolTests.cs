using Is.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Mutations.Tools;

public sealed class ReplaceMethodToolTests(ITestOutputHelper output)
    : IsolatedToolTests<RoslynMcp.Tools.Mutation.ReplaceMethod.Tool>(output)
{
    [Fact]
    public async Task Run_WithValidReplacement_ReplacesMethodAndReturnsNewSymbol()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.Run(CancellationToken.None, targetMethodSymbolId, "Assess", "string", "public", Array.Empty<string>(), ["string input", "int priority", "bool isEnabled", "string tag"], "var resultTag = tag;\r\nreturn resultTag;");

        result.Error.IsNull();
        result.Status.Is("applied");
        result.ChangedFiles.Count.Is(1);
        result.ReplacedMethod.IsNotNull();
        result.TargetMethodSymbolId.ShouldNotBeEmpty();
        result.ReplacedMethod!.OriginalSymbolId.Is(targetMethodSymbolId);
        result.ReplacedMethod.NewSymbolId.ShouldNotBeEmpty();
        string.Equals(result.ReplacedMethod.NewSymbolId, targetMethodSymbolId, StringComparison.Ordinal).IsFalse();
        result.ReplacedMethod.NewSignature.Contains("Assess", StringComparison.Ordinal).IsTrue();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Assess(string input, int priority, bool isEnabled, string tag)", StringComparison.Ordinal).IsTrue();
        text.Contains("var resultTag = tag;", StringComparison.Ordinal).IsTrue();
        text.Contains("return resultTag;", StringComparison.Ordinal).IsTrue();
        text.Contains("Evaluate", StringComparison.Ordinal).IsFalse();

        var oldResolution = await resolver.Run(CancellationToken.None, symbolId: targetMethodSymbolId);
        oldResolution.Error.IsNotNull();
        oldResolution.Error!.Code.Is("symbol_not_found");

        var newResolution = await resolver.Run(CancellationToken.None, symbolId: result.ReplacedMethod.NewSymbolId);
        newResolution.Error.IsNull();
    }

    [Fact]
    public async Task Run_WithGenericReturnType_ReplacesMethod()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.Run(CancellationToken.None, targetMethodSymbolId, "AssessAsync", "Task<int>", "public", Array.Empty<string>(), ["int priority"], "var task = Task.FromResult(priority);\nreturn task;");

        result.Error.IsNull();
        result.Status.Is("applied");
        result.ReplacedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public Task<int> AssessAsync(int priority)", StringComparison.Ordinal).IsTrue();
        text.Contains("var task = Task.FromResult(priority);", StringComparison.Ordinal).IsTrue();
        text.Contains("return task;", StringComparison.Ordinal).IsTrue();
        text.Contains("public string Evaluate", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task Run_WithEscapedGenericTypeSyntax_ReplacesMethod()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.Run(CancellationToken.None, targetMethodSymbolId, "AssessEscapedAsync", "Task&lt;int&gt;", "public", Array.Empty<string>(), ["Task&lt;int&gt; task"], "var current = task;\rreturn current;");

        result.Error.IsNull();
        result.Status.Is("applied");
        result.ReplacedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public Task<int> AssessEscapedAsync(Task<int> task)", StringComparison.Ordinal).IsTrue();
        text.Contains("var current = task;", StringComparison.Ordinal).IsTrue();
        text.Contains("return current;", StringComparison.Ordinal).IsTrue();
        text.Contains("public string Evaluate", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task Run_WithUnknownTargetSymbolId_ReturnsSymbolNotFoundWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.Run(CancellationToken.None, "not-a-real-symbol-id", "Assess", "string", "public", Array.Empty<string>(), ["string input"], "return input;");

        result.Error.IsNotNull();
        result.Error!.Code.Is("symbol_not_found");
        result.Status.Is("failed");
        result.ReplacedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task Run_WithNonMethodSymbol_ReturnsUnsupportedSymbolKind()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var resolvedType = await resolver.Run(CancellationToken.None, qualifiedName: "ProjectApp.MethodMutationTestTarget", projectName: "ProjectApp");

        resolvedType.Error.IsNull();
        resolvedType.Symbol.IsNotNull();

        var result = await sut.Run(CancellationToken.None, resolvedType.Symbol!.SymbolId, "Assess", "string", "public", Array.Empty<string>(), ["string input"], "return input;");

        result.Error.IsNotNull();
        result.Error!.Code.Is("unsupported_symbol_kind");
        result.Status.Is("failed");
        result.ReplacedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task Run_WithIntroducedCompilerDiagnostic_ReturnsChangedDocumentDelta()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.Run(CancellationToken.None, targetMethodSymbolId, "Evaluate", "string", "public", Array.Empty<string>(), ["string input", "int priority", "bool isEnabled"], "return missingName;");

        result.Error.IsNull();
        result.Status.Is("applied");
        result.ReplacedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.Any(static diagnostic => diagnostic.Id == "CS0103").IsTrue();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();
        result.DiagnosticsDelta.NewErrors.All(diagnostic => diagnostic.FilePath.HasPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"))).IsTrue();
    }

    [Fact]
    public async Task Run_WithMetadataMethod_ReturnsTargetNotSourceEditable()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "AppOrchestrator");
        var metadataMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 61, 21);

        var result = await sut.Run(CancellationToken.None, metadataMethodSymbolId, "Assess", "string", "public", Array.Empty<string>(), ["string input"], "return input;");

        result.Error.IsNotNull();
        result.Error!.Code.Is("target_not_source_editable");
        result.Status.Is("failed");
        result.ReplacedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    private static async Task<string> ResolveMethodSymbolIdAsync(IsolatedSandboxContext context, string path, int line, int column)
    {
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var resolved = await resolver.Run(CancellationToken.None, path: path, line: line, column: column);

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();
        return resolved.Symbol!.SymbolId;
    }
}
