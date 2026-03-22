using Is.Assertions;
using RoslynMcp.Tools.Tests.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

[Collection(CurrentDirectorySensitiveCollection.Name)]
public sealed class FindUsagesToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<RoslynMcp.Tools.Inspection.FindUsages.Tool>(fixture, output), IClassFixture<SharedSandboxFixture>
{
    [Fact]
    public async Task Run_WithWorkspaceRelativeDocumentPath_ReturnsRelativeReferences()
    {
        await using var context = await WorkspaceRootSandboxContext.CreateAsync();
        var resolver = context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var sut = context.GetRequiredService<RoslynMcp.Tools.Inspection.FindUsages.Tool>();
        var symbol = await resolver.Run(CancellationToken.None, path: Path.Combine("ProjectCore", "Contracts.cs"), line: 31, column: 24);

        symbol.Error.IsNull();

        var result = await sut.Run(CancellationToken.None, symbol.Symbol!.SymbolId, scope: "document", path: Path.Combine("ProjectApp", "AppOrchestrator.cs"));

        result.Error.IsNull();
        result.ReferenceFiles.Count.Is(1);
        result.ReferenceFiles[0].FilePath.Is(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
    }

    [Fact]
    public async Task Run_WithSolutionScope_ReturnsOrderedReferences()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();
        var result = await Sut.Run(CancellationToken.None, symbolId, scope: "solution");

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.Symbol!.Display.Is("IWorkItemOperation");
        result.TotalCount.Is(4);
        result.ReferenceFiles.ShouldMatchReferences(
            (Path.Combine("ProjectApp", "AppOrchestrator.cs"), 6),
            (Path.Combine("ProjectApp", "AppOrchestrator.cs"), 10),
            (Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 15),
            (Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 38));
    }

    [Fact]
    public async Task Run_WithProjectScope_ExcludesCrossProjectReferences()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();
        var result = await Sut.Run(CancellationToken.None, symbolId, scope: "project");

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.TotalCount.Is(0);
        result.ReferenceFiles.IsEmpty();
    }

    [Fact]
    public async Task Run_WithDocumentScopeAndValidPath_ReturnsOnlyDocumentReferences()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();
        var result = await Sut.Run(CancellationToken.None, symbolId, scope: "document", path: AppOrchestratorPath);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.TotalCount.Is(2);
        result.ReferenceFiles.ShouldMatchReferences(
            (Path.Combine("ProjectApp", "AppOrchestrator.cs"), 6),
            (Path.Combine("ProjectApp", "AppOrchestrator.cs"), 10));
    }

    [Fact]
    public async Task Run_WithDocumentScopeWithoutPath_ReturnsValidationError()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();
        var result = await Sut.Run(CancellationToken.None, symbolId, scope: "document");

        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_request");
        result.TotalCount.Is(0);
        result.ReferenceFiles.IsEmpty();
    }

    [Fact]
    public async Task Run_WithDocumentScopeAndInvalidPath_ReturnsInvalidPathError()
    {
        var symbolId = await ResolveWorkItemOperationSymbolIdAsync();
        var result = await Sut.Run(CancellationToken.None, symbolId, scope: "document", path: Path.Combine(TestSolutionDirectory, "ProjectApp", "Missing.cs"));

        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_path");
        result.Symbol.IsNull();
        result.TotalCount.Is(0);
        result.ReferenceFiles.IsEmpty();
    }

    [Fact]
    public async Task Run_WithInvalidScope_ReturnsValidationError()
    {
        var result = await Sut.Run(CancellationToken.None, "symbol-id", scope: "invalid");
        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_request");
        result.TotalCount.Is(0);
        result.ReferenceFiles.IsEmpty();
    }

    [Theory]
    [InlineData("not-a-real-symbol-id", "symbol_not_found")]
    [InlineData("   ", "invalid_input")]
    public async Task Run_WithUnresolvedOrInvalidSymbolId_ReturnsExpectedError(string symbolId, string expectedErrorCode)
    {
        var result = await Sut.Run(CancellationToken.None, symbolId, scope: "solution");
        result.Error.IsNotNull();
        result.Error!.Code.Is(expectedErrorCode);
        result.Symbol.IsNull();
        result.TotalCount.Is(0);
        result.ReferenceFiles.IsEmpty();
    }

    private async Task<string> ResolveWorkItemOperationSymbolIdAsync()
    {
        var resolver = Context.GetRequiredService<RoslynMcp.Tools.Inspection.ResolveSymbol.Tool>();
        var resolved = await resolver.Run(CancellationToken.None, path: ContractsPath, line: 31, column: 24);

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();
        return resolved.Symbol!.SymbolId;
    }
}

file static class AssertionExtensions
{
    internal static void ShouldMatchReferences(this IReadOnlyList<RoslynMcp.Tools.Inspection.FindUsages.ReferenceFileGroup> actual, params (string FileName, int Line)[] expected)
    {
        var flattened = actual.SelectMany(group => group.References.Select(reference => (group.FilePath, reference.Line))).ToArray();
        flattened.Length.Is(expected.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            flattened[i].FilePath.ShouldEndWithPathSuffix(expected[i].FileName);
            flattened[i].Line.Is(expected[i].Line);
        }
    }
}

file sealed class WorkspaceRootSandboxContext : SandboxContext
{
    public static async Task<WorkspaceRootSandboxContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        var context = new WorkspaceRootSandboxContext();
        try
        {
            var sandbox = TestSolutionSandbox.Create(context.CanonicalTestSolutionDirectory);
            using var currentDirectory = new CurrentDirectoryScope(sandbox.SolutionRoot);
            await context.InitializeSandboxAsync(sandbox, cancellationToken).ConfigureAwait(false);
            return context;
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

file sealed class CurrentDirectoryScope : IDisposable
{
    private readonly string _originalDirectory;

    public CurrentDirectoryScope(string currentDirectory)
    {
        _originalDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(currentDirectory);
    }

    public void Dispose()
        => Directory.SetCurrentDirectory(Directory.Exists(_originalDirectory) ? _originalDirectory : AppContext.BaseDirectory);
}
