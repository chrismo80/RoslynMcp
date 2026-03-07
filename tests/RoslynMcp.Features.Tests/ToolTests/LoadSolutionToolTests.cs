using Is.Assertions;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class LoadSolutionToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<LoadSolutionTool>(fixture, output)
{
    [Fact]
    public void LoadSolutionAsync_WithAbsoluteSolutionPath_LoadsExpectedProjects()
    {
        var result = Context.LoadedSolution;

        result.SelectedSolutionPath.Is(Context.SolutionPath);
        string.Equals(Context.SolutionPath, Context.CanonicalSolutionPath, StringComparison.OrdinalIgnoreCase).IsFalse();
        result.Error.ShouldBeNone();

        var projectNames = result.Projects.Select(static project => project.Name).ToArray();
        
        projectNames.IsContaining("ProjectApp");
        projectNames.IsContaining("ProjectCore");
        projectNames.IsContaining("ProjectImpl");
    }
}

public sealed class LoadSolutionToolIsolatedTests(ITestOutputHelper output)
    : IsolatedToolTests<LoadSolutionTool>(output)
{
    [Fact]
    public async Task LoadSolutionAsync_WithSlnxSolutionPath_LoadsRepositorySolution()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var solutionPath = Path.Combine(context.RepositoryRoot, "RoslynMcp.slnx");

        var result = await sut.ExecuteAsync(CancellationToken.None, solutionPath);

        result.Error.ShouldBeNone();
        result.SelectedSolutionPath.Is(solutionPath);
        result.Projects.Any(project => project.Name == "RoslynMcp.Features").IsTrue();
        result.Projects.Any(project => project.Name == "RoslynMcp.Infrastructure").IsTrue();
    }
}
