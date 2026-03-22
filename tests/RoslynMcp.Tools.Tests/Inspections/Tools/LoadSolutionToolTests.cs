using Is.Assertions;
using RoslynMcp.Tools.Inspection.LoadSolution;
using RoslynMcp.Tools.Tests.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

public sealed class LoadSolutionToolTests(ITestOutputHelper output)
    : IsolatedToolTests<Tool>(output)
{
    [Fact]
    public async Task Run_WithAbsoluteSolutionPath_LoadsExpectedProjects()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.Run(CancellationToken.None, context.SolutionPath);

        result.Error.IsNull();
        result.SelectedSolutionPath.Is(context.SolutionPath);
        result.Projects.Any(project => project.Path is not null && project.Path.HasPathSuffix(Path.Combine("ProjectApp", "ProjectApp.csproj"))).IsTrue();
        var projectNames = result.Projects.Select(static project => project.Name).ToArray();
        projectNames.IsContaining("ProjectApp");
        projectNames.IsContaining("ProjectCore");
        projectNames.IsContaining("ProjectImpl");
    }

    [Fact]
    public async Task Run_WithMissingGeneratedDocument_ReportsMissingArtifactsReadiness()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var generatedPath = Path.Combine(context.TestSolutionDirectory, "ProjectApp", "obj", "Debug", "net10.0", "GeneratedExecutionHooks.g.cs");

        File.Delete(generatedPath);

        var result = await sut.Run(CancellationToken.None, context.SolutionPath);

        result.Error.IsNull();
        result.Readiness.State.Is(ReadinessStates.DegradedMissingArtifacts);
        result.Readiness.DegradedReasons.IsContaining("missing_artifacts");
        result.Readiness.RecommendedNextStep.IsNotNull();
    }
}
