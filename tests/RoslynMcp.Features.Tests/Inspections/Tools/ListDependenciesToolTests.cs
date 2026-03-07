using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class ListDependenciesToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<ListDependenciesTool>(fixture, output)
{
    [Fact]
    public async Task ListDependenciesAsync_WithOutgoingDirection_ReturnsOutgoingDependencies()
    {
        var project = Context.GetProject("ProjectApp");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectPath: project.Path, direction: "outgoing");

        result.Error.ShouldBeNone();
        result.TotalCount.Is(2);
        result.Dependencies.Select(static dependency => dependency.ProjectName).Is("ProjectCore", "ProjectImpl");
        result.Edges!.Select(static edge => $"{edge.Source.ProjectName}->{edge.Target.ProjectName}").Is("ProjectApp->ProjectCore", "ProjectApp->ProjectImpl");
    }

    [Fact]
    public async Task ListDependenciesAsync_WithIncomingDirection_ReturnsIncomingDependencies()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectName: project.Name, direction: "incoming");

        result.Error.ShouldBeNone();
        result.TotalCount.Is(2);
        result.Dependencies.Select(dependency => dependency.ProjectName).Is("ProjectApp", "ProjectImpl");
        result.Edges!.Select(static edge => $"{edge.Source.ProjectName}->{edge.Target.ProjectName}").Is("ProjectApp->ProjectCore", "ProjectImpl->ProjectCore");
    }

    [Fact]
    public async Task ListDependenciesAsync_WithBothDirection_ReturnsOutgoingAndIncomingDependencies()
    {
        var project = Context.GetProject("ProjectImpl");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectName: project.Name, direction: "both");

        result.Error.ShouldBeNone();
        result.TotalCount.Is(2);
        result.Dependencies.Select(dependency => dependency.ProjectName).Is("ProjectApp", "ProjectCore");
        result.Edges!.Select(static edge => $"{edge.Source.ProjectName}->{edge.Target.ProjectName}").Is("ProjectApp->ProjectImpl", "ProjectImpl->ProjectCore");
    }

    [Fact]
    public async Task ListDependenciesAsync_WhenNoProjectSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task ListDependenciesAsync_WhenMultipleProjectSelectorsProvided_ReturnsValidationError()
    {
        var project = Context.GetProject("ProjectImpl");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectPath: project.Path, projectName: project.Name);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task ListDependenciesAsync_WhenProjectNotFound_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectId: Guid.NewGuid().ToString());

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}