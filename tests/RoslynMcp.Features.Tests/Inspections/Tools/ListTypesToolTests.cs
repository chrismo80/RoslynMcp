using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Infrastructure.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class ListTypesToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<ListTypesTool>(fixture, output)
{
    [Fact]
    public async Task ListTypesAsync_WithProjectNameSelector_ReturnsExpectedTypes()
    {
        var project = Context.GetProject("ProjectApp");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectName: project.Name);

        result.ShouldMatchTypes(2, "AppEntryPoints", "AppOrchestrator");
    }

    [Fact]
    public async Task ListTypesAsync_WithProjectPathSelector_ReturnsExpectedTypes()
    {
        var project = Context.GetProject("ProjectApp");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectPath: project.Path);

        result.ShouldMatchTypes(2, "AppEntryPoints", "AppOrchestrator");
    }

    [Fact]
    public async Task ListTypesAsync_WithProjectIdSelector_ReturnsExpectedTypes()
    {
        var projectId = await GetProjectIdAsync("ProjectApp");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectId: projectId);

        result.ShouldMatchTypes(2, "AppEntryPoints", "AppOrchestrator");
    }

    [Fact]
    public async Task ListTypesAsync_WithNamespacePrefixThatDoesNotMatch_ReturnsNoTypes()
    {
        var project = Context.GetProject("ProjectImpl");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectName: project.Name, namespacePrefix: "ProjectImpl.Internal");

        result.ShouldMatchTypes(0);
    }

    [Fact]
    public async Task ListTypesAsync_WithKindFilter_ReturnsOnlyRecordTypes()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectName: project.Name, kind: "record");

        result.ShouldMatchTypes(2, "OperationResult", "WorkItem");
        result.Types.Select(static type => type.Kind).Distinct().Is("record");
    }

    [Fact]
    public async Task ListTypesAsync_WithAccessibilityFilter_ReturnsNoInternalTypes()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectPath: project.Path, accessibility: "internal");

        result.ShouldMatchTypes(0);
    }

    [Fact]
    public async Task ListTypesAsync_WithLimitAndOffset_PaginatesDeterministically()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectName: project.Name, limit: 4, offset: 5);

        result.ShouldMatchTypes(14, "WorkerA", "WorkerB", "IFactory<T>", "IOperation<TInput, TResult>");
    }

    [Fact]
    public async Task ListTypesAsync_WithInvalidKind_ReturnsValidationError()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectName: project.Name, kind: "delegate");

        result.ShouldHaveError(ErrorCodes.InvalidInput, "kind must be one of: class, record, interface, enum, struct.");
        result.TotalCount.Is(0);
        result.Types.Count.Is(0);
    }

    [Fact]
    public async Task ListTypesAsync_WithInvalidAccessibility_ReturnsValidationError()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectName: project.Name, accessibility: "package");

        result.ShouldHaveError(ErrorCodes.InvalidInput, "accessibility must be one of: public, internal, protected, private, protected_internal, private_protected.");
        result.TotalCount.Is(0);
        result.Types.Count.Is(0);
    }

    [Fact]
    public async Task ListTypesAsync_WhenNoProjectSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None);

        result.ShouldHaveError(ErrorCodes.InvalidInput, "A project selector is required. Provide projectPath, projectName, or projectId.");
        result.TotalCount.Is(0);
        result.Types.Count.Is(0);
    }

    [Fact]
    public async Task ListTypesAsync_WithUnknownProjectId_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectId: "00000000-0000-0000-0000-000000000000");

        result.ShouldHaveError(ErrorCodes.InvalidInput, "projectId did not match any project in the current loaded snapshot.");
        result.TotalCount.Is(0);
        result.Types.Count.Is(0);
        result.Error!.Details!["projectIdScope"].Is("snapshot-local");
    }

    private async Task<string> GetProjectIdAsync(string projectName)
    {
        var accessor = Context.GetRequiredService<IRoslynSolutionAccessor>();
        var (solution, error) = await accessor.GetCurrentSolutionAsync(CancellationToken.None);

        error.ShouldBeNone();
        solution.IsNotNull();

        return solution!.Projects.Single(project => project.Name == projectName).Id.Id.ToString();
    }
}

internal static class ListTypesToolTestAssertions
{
    public static void ShouldMatchTypes(this ListTypesResult result, int expectedTotalCount, params string[] expectedDisplayNames)
    {
        result.Error.ShouldBeNone();
        result.TotalCount.Is(expectedTotalCount);
        result.Types.Select(static type => type.DisplayName).Is(expectedDisplayNames);
    }

    public static void ShouldHaveError(this ListTypesResult result, string expectedCode, string expectedMessage)
    {
        result.Error.ShouldHaveCode(expectedCode);
        result.Error!.Message.Is(expectedMessage);
    }
}
