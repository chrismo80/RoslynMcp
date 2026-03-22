using Is.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Mutations.Tools;

public sealed class FormatDocumentToolTests(ITestOutputHelper output)
    : IsolatedToolTests<RoslynMcp.Tools.Mutation.FormatDocument.Tool>(output)
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Run_WithEmptyOrWhitespacePath_ReturnsValidationError(string path)
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.Run(CancellationToken.None, path);

        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_input");
        result.WasFormatted.IsFalse();
    }

    [Theory]
    [InlineData("/tmp/OutsideSolution.cs")]
    [InlineData("ProjectImpl/MissingFile.cs")]
    public async Task Run_WithPathOutOfScopeOrMissing_ReturnsPathOutOfScope(string path)
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var requestedPath = Path.IsPathRooted(path) ? path : Path.Combine(context.TestSolutionDirectory, path);

        var result = await sut.Run(CancellationToken.None, requestedPath);

        result.Error.IsNotNull();
        result.Error!.Code.Is("path_out_of_scope");
        result.WasFormatted.IsFalse();
    }

    [Fact]
    public async Task Run_WithDirectDiskEditAfterLoad_PreservesEditWhileFormatting()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectImpl", "FormattingFixture");

        await File.WriteAllTextAsync(filePath, "namespace ProjectImpl;\r\n\r\npublic sealed class FormattingFixture\r\n{\r\npublic int Add( int left,int right )\r\n    {\r\n            return left+right+1;\r\n    }\r\n}\r\n");

        var result = await sut.Run(CancellationToken.None, filePath);

        result.Error.IsNull();
        result.Path.ShouldEndWithPathSuffix(Path.Combine("ProjectImpl", "FormattingFixture.cs"));
        result.WasFormatted.IsTrue();

        var after = await File.ReadAllTextAsync(filePath);
        after.Contains("public int Add(int left, int right)", StringComparison.Ordinal).IsTrue();
        after.Contains("return left + right + 1;", StringComparison.Ordinal).IsTrue();
        after.Contains("return left + right;", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task Run_WithUnreadableFileDuringHealthCheck_ReturnsStaleWorkspaceSnapshot()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectImpl", "FormattingFixture");

        await using var lockStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await sut.Run(CancellationToken.None, filePath);

        result.Error.IsNotNull();
        result.Error!.Code.Is("stale_workspace_snapshot");
        result.WasFormatted.IsFalse();
    }
}

[Collection(CurrentDirectorySensitiveCollection.Name)]
public sealed class FormatDocumentToolCurrentDirectoryTests(ITestOutputHelper output)
    : IsolatedToolTests<RoslynMcp.Tools.Mutation.FormatDocument.Tool>(output)
{
    [Fact]
    public async Task Run_WithAbsoluteInWorkspacePath_ReturnsRelativeSuccessPath()
    {
        await using var context = await WorkspaceRootSandboxContext.CreateAsync();
        var sut = context.GetRequiredService<RoslynMcp.Tools.Mutation.FormatDocument.Tool>();
        var filePath = context.GetFilePath("ProjectCore", "Contracts");
        var before = await File.ReadAllTextAsync(filePath);

        var result = await sut.Run(CancellationToken.None, filePath);

        result.Error.IsNull();
        result.Path.Is(Path.Combine("ProjectCore", "Contracts.cs"));
        result.WasFormatted.IsFalse();

        var after = await File.ReadAllTextAsync(filePath);
        after.Is(before);
    }

    [Fact]
    public async Task Run_WithWorkspaceRelativeInWorkspacePath_FormatsAndPersistsChanges()
    {
        await using var context = await WorkspaceRootSandboxContext.CreateAsync();
        var sut = context.GetRequiredService<RoslynMcp.Tools.Mutation.FormatDocument.Tool>();
        var filePath = context.GetFilePath("ProjectImpl", "FormattingFixture");
        var before = await File.ReadAllTextAsync(filePath);
        var relativePath = Path.Combine("ProjectImpl", "FormattingFixture.cs");

        var result = await sut.Run(CancellationToken.None, relativePath);

        result.Error.IsNull();
        result.Path.Is(relativePath);
        result.WasFormatted.IsTrue();

        var after = await File.ReadAllTextAsync(filePath);
        string.Equals(after, before, StringComparison.Ordinal).IsFalse();
        after.Contains("public int Add(int left, int right)", StringComparison.Ordinal).IsTrue();
        after.Contains("return left + right;", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task Run_WithWorkspaceRelativeMissingPath_ReturnsRelativePathOutOfScope()
    {
        await using var context = await WorkspaceRootSandboxContext.CreateAsync();
        var sut = context.GetRequiredService<RoslynMcp.Tools.Mutation.FormatDocument.Tool>();
        var relativePath = Path.Combine("ProjectImpl", "MissingFile.cs");

        var result = await sut.Run(CancellationToken.None, relativePath);

        result.Error.IsNotNull();
        result.Error!.Code.Is("path_out_of_scope");
        result.Path.Is(relativePath);
        result.Error.Details!.ContainsKey("path").IsTrue();
        result.Error.Details["path"].Is(relativePath);
        result.WasFormatted.IsFalse();
    }

    [Fact]
    public async Task Run_WithWorkspaceRelativePath_ForNestedSolution_FormatsAndReturnsWorkspaceRelativePath()
    {
        await using var context = await NestedWorkspaceRootSandboxContext.CreateAsync();
        var sut = context.GetRequiredService<RoslynMcp.Tools.Mutation.FormatDocument.Tool>();
        var filePath = context.GetFilePath("ProjectImpl", "FormattingFixture");
        var relativePath = Path.Combine("tests", "TestSolution", "ProjectImpl", "FormattingFixture.cs");

        var result = await sut.Run(CancellationToken.None, relativePath);

        result.Error.IsNull();
        result.Path.Is(relativePath);
        result.WasFormatted.IsTrue();

        var after = await File.ReadAllTextAsync(filePath);
        after.Contains("public int Add(int left, int right)", StringComparison.Ordinal).IsTrue();
        after.Contains("return left + right;", StringComparison.Ordinal).IsTrue();
    }

    private sealed class CurrentDirectoryScope(string originalDirectory) : IDisposable
    {
        public static CurrentDirectoryScope Enter(string currentDirectory)
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(currentDirectory);
            return new CurrentDirectoryScope(originalDirectory);
        }

        public void Dispose()
            => Directory.SetCurrentDirectory(Directory.Exists(originalDirectory) ? originalDirectory : AppContext.BaseDirectory);
    }

    private sealed class WorkspaceRootSandboxContext : SandboxContext
    {
        public static async Task<WorkspaceRootSandboxContext> CreateAsync(CancellationToken cancellationToken = default)
        {
            var context = new WorkspaceRootSandboxContext();
            try
            {
                var sandbox = TestSolutionSandbox.Create(context.CanonicalTestSolutionDirectory);
                using var currentDirectory = CurrentDirectoryScope.Enter(sandbox.SolutionRoot);
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

    private sealed class NestedWorkspaceRootSandboxContext : SandboxContext
    {
        public static async Task<NestedWorkspaceRootSandboxContext> CreateAsync(CancellationToken cancellationToken = default)
        {
            var context = new NestedWorkspaceRootSandboxContext();
            try
            {
                var sandbox = TestSolutionSandbox.CreateNested(context.CanonicalTestSolutionDirectory, "tests", "TestSolution");
                using var currentDirectory = CurrentDirectoryScope.Enter(sandbox.SandboxRoot);
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
}
