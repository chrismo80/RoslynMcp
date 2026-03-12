using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools.Inspections;
using RoslynMcp.Features.Tools.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Mutations.Tools;

public sealed class DeleteMethodToolTests(ITestOutputHelper output)
    : IsolatedToolTests<DeleteMethodTool>(output)
{
    [Fact]
    public async Task ExecuteAsync_WithValidMethod_DeletesMethodAndReturnsDeletedMethodInfo()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.ExecuteAsync(CancellationToken.None, targetMethodSymbolId);

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.ChangedFiles.Count.Is(1);
        result.ChangedFiles[0].ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"));
        result.DeletedMethod.IsNotNull();
        result.TargetMethodSymbolId.ShouldBeExternalSymbolId();
        result.DeletedMethod!.SymbolId.Is(targetMethodSymbolId);
        result.DeletedMethod.Signature.Contains("Evaluate", StringComparison.Ordinal).IsTrue();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("Evaluate", StringComparison.Ordinal).IsFalse();

        var deletedResolution = await resolver.ExecuteAsync(CancellationToken.None, symbolId: targetMethodSymbolId);
        deletedResolution.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownTargetSymbolId_ReturnsSymbolNotFoundWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.ExecuteAsync(CancellationToken.None, "not-a-real-symbol-id");

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.Status.Is("failed");
        result.DeletedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithNonMethodSymbol_ReturnsUnsupportedSymbolKind()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var resolvedType = await resolver.ExecuteAsync(
            CancellationToken.None,
            qualifiedName: "ProjectApp.MethodMutationTestTarget",
            projectName: "ProjectApp");

        resolvedType.Error.ShouldBeNone();
        resolvedType.Symbol.IsNotNull();

        var result = await sut.ExecuteAsync(CancellationToken.None, resolvedType.Symbol!.SymbolId);

        result.Error.ShouldHaveCode(ErrorCodes.UnsupportedSymbolKind);
        result.Status.Is("failed");
        result.DeletedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithIntroducedCompilerDiagnostic_ReturnsChangedDocumentDelta()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "AppOrchestrator");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 54, 35);

        var result = await sut.ExecuteAsync(CancellationToken.None, targetMethodSymbolId);

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.DeletedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.Any(static diagnostic => diagnostic.Id == "CS0103").IsTrue();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();
        result.DiagnosticsDelta.NewErrors.All(diagnostic => diagnostic.FilePath.HasPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"))).IsTrue();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("private Task<OperationResult> ExecuteFlowAsync", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithDirectDiskEditAfterLoad_ReturnsStaleWorkspaceSnapshotAndPreservesFile()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        await File.WriteAllTextAsync(
            filePath,
            "namespace ProjectApp;\r\n\r\npublic sealed class MethodMutationTestTarget\r\n{\r\n    private const string DirectDiskEditMarker = \"delete-method-stale-edit\";\r\n\r\n    public string Evaluate(string input, int priority, bool isEnabled)\r\n    {\r\n        return string.Empty;\r\n    }\r\n}\r\n");

        var result = await sut.ExecuteAsync(CancellationToken.None, targetMethodSymbolId);

        result.Error.ShouldHaveCode(ErrorCodes.StaleWorkspaceSnapshot);
        result.Status.Is("failed");
        result.DeletedMethod.IsNull();
        result.ChangedFiles.Count.Is(1);
        result.ChangedFiles[0].ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"));

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("DirectDiskEditMarker = \"delete-method-stale-edit\"", StringComparison.Ordinal).IsTrue();
        text.Contains("public string Evaluate(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithMetadataMethod_ReturnsTargetNotSourceEditable()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "AppOrchestrator");
        var metadataMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 61, 21);

        var result = await sut.ExecuteAsync(CancellationToken.None, metadataMethodSymbolId);

        result.Error.ShouldHaveCode(ErrorCodes.TargetNotSourceEditable);
        result.Status.Is("failed");
        result.DeletedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    private static async Task<string> ResolveMethodSymbolIdAsync(IsolatedSandboxContext context, string path, int line, int column)
    {
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: path, line: line, column: column);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();
        return resolved.Symbol!.SymbolId;
    }
}
