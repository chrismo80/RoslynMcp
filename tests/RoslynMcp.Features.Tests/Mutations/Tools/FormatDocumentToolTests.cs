using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Mutations.Tools;

public sealed class FormatDocumentToolTests(ITestOutputHelper output)
    : IsolatedToolTests<FormatDocumentTool>(output)
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WithEmptyOrWhitespacePath_ReturnsValidationError(string path)
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.ExecuteAsync(CancellationToken.None, path);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
        result.WasFormatted.IsFalse();
    }

    [Theory]
    [InlineData("/tmp/OutsideSolution.cs")]
    [InlineData("ProjectImpl/MissingFile.cs")]
    public async Task ExecuteAsync_WithPathOutOfScopeOrMissing_ReturnsPathOutOfScope(string path)
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var requestedPath = Path.IsPathRooted(path) ? path : Path.Combine(context.TestSolutionDirectory, path);

        var result = await sut.ExecuteAsync(CancellationToken.None, requestedPath);

        result.Error.ShouldHaveCode(ErrorCodes.PathOutOfScope);
        result.WasFormatted.IsFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithAlreadyFormattedFile_ReturnsSuccessWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectCore", "Contracts");
        var before = await File.ReadAllTextAsync(filePath);

        var result = await sut.ExecuteAsync(CancellationToken.None, filePath);

        result.Error.ShouldBeNone();
        result.Path.ShouldEndWithPathSuffix(Path.Combine("ProjectCore", "Contracts.cs"));
        result.WasFormatted.IsFalse();

        var after = await File.ReadAllTextAsync(filePath);
        after.Is(before);
    }

    [Fact]
    public async Task ExecuteAsync_WithMisformattedFile_FormatsAndPersistsChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectImpl", "FormattingFixture");
        var before = await File.ReadAllTextAsync(filePath);

        var result = await sut.ExecuteAsync(CancellationToken.None, filePath);

        result.Error.ShouldBeNone();
        result.Path.ShouldEndWithPathSuffix(Path.Combine("ProjectImpl", "FormattingFixture.cs"));
        result.WasFormatted.IsTrue();

        var after = await File.ReadAllTextAsync(filePath);
        string.Equals(after, before, StringComparison.Ordinal).IsFalse();
        after.Contains("public int Add(int left, int right)", StringComparison.Ordinal).IsTrue();
        after.Contains("return left + right;", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithDirectDiskEditAfterLoad_PreservesEditWhileFormatting()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectImpl", "FormattingFixture");

        await File.WriteAllTextAsync(
            filePath,
            "namespace ProjectImpl;\r\n\r\npublic sealed class FormattingFixture\r\n{\r\npublic int Add( int left,int right )\r\n    {\r\n            return left+right+1;\r\n    }\r\n}\r\n");

        var result = await sut.ExecuteAsync(CancellationToken.None, filePath);

        result.Error.ShouldBeNone();
        result.Path.ShouldEndWithPathSuffix(Path.Combine("ProjectImpl", "FormattingFixture.cs"));
        result.WasFormatted.IsTrue();

        var after = await File.ReadAllTextAsync(filePath);
        after.Contains("public int Add(int left, int right)", StringComparison.Ordinal).IsTrue();
        after.Contains("return left + right + 1;", StringComparison.Ordinal).IsTrue();
        after.Contains("return left + right;", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithUnreadableFileDuringHealthCheck_ReturnsStaleWorkspaceSnapshot()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectImpl", "FormattingFixture");

        await using var lockStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await sut.ExecuteAsync(CancellationToken.None, filePath);

        result.Error.ShouldHaveCode(ErrorCodes.StaleWorkspaceSnapshot);
        result.WasFormatted.IsFalse();
    }
}
