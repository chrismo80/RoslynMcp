using Is.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Mutations.Tools;

public sealed class DeleteMethodToolTests(ITestOutputHelper output)
    : IsolatedToolTests<RoslynMcp.Tools.Mutation.DeleteMethod.Tool>(output)
{
    [Fact]
    public async Task Run_WithValidMethod_DeletesMethodAndReturnsDeletedMethodInfo()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.Run(CancellationToken.None, targetMethodSymbolId);

        result.Error.IsNull();
        result.Status.Is("applied");
        result.ChangedFiles.Count.Is(1);
        result.ChangedFiles[0].ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"));
        result.DeletedMethod.IsNotNull();
        result.TargetMethodSymbolId.ShouldNotBeEmpty();
        result.DeletedMethod!.SymbolId.Is(targetMethodSymbolId);
        result.DeletedMethod.Signature.Contains("Evaluate", StringComparison.Ordinal).IsTrue();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("Evaluate", StringComparison.Ordinal).IsFalse();

        var deletedResolution = await resolver.Run(CancellationToken.None, symbolId: targetMethodSymbolId);
        deletedResolution.Error.IsNotNull();
        deletedResolution.Error!.Code.Is("symbol_not_found");
    }

    [Fact]
    public async Task Run_WithUnknownTargetSymbolId_ReturnsSymbolNotFoundWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.Run(CancellationToken.None, "not-a-real-symbol-id");

        result.Error.IsNotNull();
        result.Error!.Code.Is("symbol_not_found");
        result.Status.Is("failed");
        result.DeletedMethod.IsNull();
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

        var result = await sut.Run(CancellationToken.None, resolvedType.Symbol!.SymbolId);

        result.Error.IsNotNull();
        result.Error!.Code.Is("unsupported_symbol_kind");
        result.Status.Is("failed");
        result.DeletedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task Run_WithIntroducedCompilerDiagnostic_ReturnsChangedDocumentDelta()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "AppOrchestrator");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 54, 35);

        var result = await sut.Run(CancellationToken.None, targetMethodSymbolId);

        result.Error.IsNull();
        result.Status.Is("applied");
        result.DeletedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.Any(static diagnostic => diagnostic.Id == "CS0103").IsTrue();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();
        result.DiagnosticsDelta.NewErrors.All(diagnostic => diagnostic.FilePath.HasPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"))).IsTrue();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("private Task<OperationResult> ExecuteFlowAsync", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task Run_WithDirectDiskEditAfterLoad_ReturnsStaleWorkspaceSnapshotAndPreservesFile()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        await File.WriteAllTextAsync(filePath, "namespace ProjectApp;\r\n\r\npublic sealed class MethodMutationTestTarget\r\n{\r\n    private const string DirectDiskEditMarker = \"delete-method-stale-edit\";\r\n\r\n    public string Evaluate(string input, int priority, bool isEnabled)\r\n    {\r\n        return string.Empty;\r\n    }\r\n}\r\n");

        var result = await sut.Run(CancellationToken.None, targetMethodSymbolId);

        result.Error.IsNotNull();
        result.Error!.Code.Is("stale_workspace_snapshot");
        result.Status.Is("failed");
        result.DeletedMethod.IsNull();
        result.ChangedFiles.Count.Is(1);
        result.ChangedFiles[0].ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"));

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("DirectDiskEditMarker = \"delete-method-stale-edit\"", StringComparison.Ordinal).IsTrue();
        text.Contains("public string Evaluate(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task Run_WithMetadataMethod_ReturnsTargetNotSourceEditable()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "AppOrchestrator");
        var metadataMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 61, 21);

        var result = await sut.Run(CancellationToken.None, metadataMethodSymbolId);

        result.Error.IsNotNull();
        result.Error!.Code.Is("target_not_source_editable");
        result.Status.Is("failed");
        result.DeletedMethod.IsNull();
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
