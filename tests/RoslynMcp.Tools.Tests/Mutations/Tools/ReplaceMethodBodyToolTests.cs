using Microsoft.Extensions.DependencyInjection;
using Is.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Mutations.Tools;

public sealed class ReplaceMethodBodyToolTests(ITestOutputHelper output)
    : IsolatedToolTests<RoslynMcp.Tools.Mutation.ReplaceMethodBody.Tool>(output)
{
    [Fact]
    public async Task Run_WithValidBody_ReplacesBodyAndPreservesSignature()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.Run(CancellationToken.None, targetMethodSymbolId, "var combined = input + priority.ToString();\r\nreturn combined;");

        result.Error.IsNull();
        result.Status.Is("applied");
        result.ChangedFiles.Count.Is(1);
        result.ReplacedMethodBody.IsNotNull();
        result.TargetMethodSymbolId.ShouldNotBeEmpty();
        result.ReplacedMethodBody!.MethodSymbolId.ShouldNotBeEmpty();
        result.ReplacedMethodBody.MethodSymbolId.Is(targetMethodSymbolId);
        result.ReplacedMethodBody.Signature.Contains("Evaluate", StringComparison.Ordinal).IsTrue();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Evaluate(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
        text.Contains("var combined = input + priority.ToString();", StringComparison.Ordinal).IsTrue();
        text.Contains("return combined;", StringComparison.Ordinal).IsTrue();
        text.Contains("return string.Empty;", StringComparison.Ordinal).IsFalse();

        var resolved = await resolver.Run(CancellationToken.None, symbolId: result.ReplacedMethodBody.MethodSymbolId);
        resolved.Error.IsNull();
    }

    [Fact]
    public async Task Run_WithUnknownTargetSymbolId_ReturnsSymbolNotFoundWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.Run(CancellationToken.None, "not-a-real-symbol-id", "return string.Empty;");

        result.Error.IsNotNull();
        result.Error!.Code.Is("symbol_not_found");
        result.Status.Is("failed");
        result.ReplacedMethodBody.IsNull();
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

        var result = await sut.Run(CancellationToken.None, resolvedType.Symbol!.SymbolId, "return string.Empty;");

        result.Error.IsNotNull();
        result.Error!.Code.Is("unsupported_symbol_kind");
        result.Status.Is("failed");
        result.ReplacedMethodBody.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task Run_WithIntroducedCompilerDiagnostic_ReturnsChangedDocumentDelta()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.Run(CancellationToken.None, targetMethodSymbolId, "return missingName;");

        result.Error.IsNull();
        result.Status.Is("applied");
        result.ReplacedMethodBody.IsNotNull();
        result.DiagnosticsDelta.NewErrors.Any(static diagnostic => diagnostic.Id == "CS0103").IsTrue();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();
        result.DiagnosticsDelta.NewErrors.All(diagnostic => diagnostic.FilePath.HasPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"))).IsTrue();
    }

    [Fact]
    public async Task Run_AfterAddMethodInSameSession_ReplacesBodyWithoutApplyFailure()
    {
        await using var context = await CreateContextAsync();
        var replaceMethodBodyTool = GetSut(context);
        var addMethodTool = context.GetRequiredService<RoslynMcp.Tools.Mutation.AddMethod.Tool>();
        var targetTypeSymbolId = await ResolveMethodMutationTestTargetAsync(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var addResult = await addMethodTool.Run(CancellationToken.None, targetTypeSymbolId, "Plan", "string", "public", Array.Empty<string>(), ["string input", "int priority", "bool isEnabled"], "var plan = string.Empty;\nreturn plan;");

        addResult.Error.IsNull();
        addResult.Status.Is("applied");

        var replaceResult = await replaceMethodBodyTool.Run(CancellationToken.None, targetMethodSymbolId, "var updated = input + \"-updated\";\r\nreturn updated;");

        replaceResult.Error.IsNull();
        replaceResult.Status.Is("applied");
        replaceResult.ReplacedMethodBody.IsNotNull();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Plan(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
        text.Contains("var updated = input + \"-updated\";", StringComparison.Ordinal).IsTrue();
        text.Contains("return updated;", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task Run_WhenApplyFails_KeepsOriginalSymbolIdUsable()
    {
        await using var context = await ReplaceMethodBodyApplyFailureSandboxContext.CreateAsync();
        var sut = context.GetRequiredService<RoslynMcp.Tools.Mutation.ReplaceMethodBody.Tool>();
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);
        var originalText = await File.ReadAllTextAsync(filePath);

        var failed = await sut.Run(CancellationToken.None, targetMethodSymbolId, "var updated = input + \"-updated\";\r\nreturn updated;");

        failed.Error.IsNotNull();
        failed.Error!.Code.Is("internal_error");
        failed.Status.Is("failed");
        failed.ReplacedMethodBody.IsNull();
        context.ApplyInterceptor.ApplyAttempts.Is(1);

        var resolution = await resolver.Run(CancellationToken.None, symbolId: targetMethodSymbolId);

        resolution.Error.IsNull();
        resolution.Symbol.IsNotNull();
        resolution.Symbol!.SymbolId.Is(targetMethodSymbolId);

        var currentText = await File.ReadAllTextAsync(filePath);
        currentText.Is(originalText);

        var succeeded = await sut.Run(CancellationToken.None, targetMethodSymbolId, "var updated = input + \"-updated\";\r\nreturn updated;");

        succeeded.Error.IsNull();
        succeeded.Status.Is("applied");
        succeeded.ReplacedMethodBody.IsNotNull();
        succeeded.ReplacedMethodBody!.MethodSymbolId.Is(targetMethodSymbolId);
    }

    [Fact]
    public async Task Run_WithMetadataMethod_ReturnsTargetNotSourceEditable()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "AppOrchestrator");
        var metadataMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 61, 21);

        var result = await sut.Run(CancellationToken.None, metadataMethodSymbolId, "return string.Empty;");

        result.Error.IsNotNull();
        result.Error!.Code.Is("target_not_source_editable");
        result.Status.Is("failed");
        result.ReplacedMethodBody.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    private static async Task<string> ResolveMethodSymbolIdAsync(SandboxContext context, string path, int line, int column)
    {
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var resolved = await resolver.Run(CancellationToken.None, path: path, line: line, column: column);

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();
        return resolved.Symbol!.SymbolId;
    }

    private static async Task<string> ResolveMethodMutationTestTargetAsync(SandboxContext context)
    {
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var resolved = await resolver.Run(CancellationToken.None, qualifiedName: "ProjectApp.MethodMutationTestTarget", projectName: "ProjectApp");

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();
        return resolved.Symbol!.SymbolId;
    }
}

file sealed class ReplaceMethodBodyApplyFailureSandboxContext : SandboxContext
{
    private ReplaceMethodBodyApplyFailureSandboxContext()
    {
    }

    public FirstApplyFailsReplaceMethodBodyWorkspace ApplyInterceptor => GetRequiredService<FirstApplyFailsReplaceMethodBodyWorkspace>();

    public static async Task<ReplaceMethodBodyApplyFailureSandboxContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        var context = new ReplaceMethodBodyApplyFailureSandboxContext();
        try
        {
            var sandbox = TestSolutionSandbox.Create(context.CanonicalTestSolutionDirectory);
            await context.InitializeSandboxAsync(sandbox, cancellationToken).ConfigureAwait(false);
            return context;
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    protected override ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.WithRoslynMcp();
        services.AddSingleton<FirstApplyFailsReplaceMethodBodyWorkspace>();
        services.AddSingleton<RoslynMcp.Tools.Infrastructure.Services.Workspace>(provider => provider.GetRequiredService<FirstApplyFailsReplaceMethodBodyWorkspace>());
        return services.BuildServiceProvider();
    }
}

file sealed class FirstApplyFailsReplaceMethodBodyWorkspace : RoslynMcp.Tools.Infrastructure.Services.Workspace
{
    private int _remainingApplyFailures = 1;

    public int ApplyAttempts { get; private set; }

    internal override bool TryApplyChanges(RoslynMcp.Tools.Infrastructure.Session session, Microsoft.CodeAnalysis.Solution solution)
    {
        ApplyAttempts++;
        if (Interlocked.Exchange(ref _remainingApplyFailures, 0) == 1)
            return false;

        return base.TryApplyChanges(session, solution);
    }
}
