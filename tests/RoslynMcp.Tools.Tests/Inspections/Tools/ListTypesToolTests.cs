using Is.Assertions;
using RoslynMcp.Tools.Tests.Inspections;
using RoslynMcp.Tools.Tests.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

public sealed class ListTypesToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<RoslynMcp.Tools.Inspection.ListTypes.Tool>(fixture, output), IClassFixture<SharedSandboxFixture>
{
    [Fact]
    public async Task Run_WithProjectNameSelector_ReturnsExpectedTypes()
    {
        var project = Context.GetProject("ProjectApp");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name);

        result.ShouldMatchTypes(3, "AppEntryPoints", "AppOrchestrator", "MethodMutationTestTarget");
    }

    [Fact]
    public async Task Run_WithProjectPathSelector_ReturnsExpectedTypes()
    {
        var project = Context.GetProject("ProjectApp");
        var result = await Sut.Run(CancellationToken.None, projectPath: project.FilePath);

        result.ShouldMatchTypes(3, "AppEntryPoints", "AppOrchestrator", "MethodMutationTestTarget");
        result.Context.SourceBias.Is("handwritten");
        result.Context.Limitations.Any(static limitation => limitation.Contains("generated declarations were omitted", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Fact]
    public async Task Run_WithProjectIdSelector_ReturnsExpectedTypes()
    {
        var projectId = Context.GetCurrentSolution().Projects.Single(project => project.Name == "ProjectApp").Id.Id.ToString();
        var result = await Sut.Run(CancellationToken.None, projectId: projectId);

        result.ShouldMatchTypes(3, "AppEntryPoints", "AppOrchestrator", "MethodMutationTestTarget");
    }

    [Fact]
    public async Task Run_WithNamespacePrefixThatDoesNotMatch_ReturnsNoTypes()
    {
        var project = Context.GetProject("ProjectImpl");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, namespacePrefix: "ProjectImpl.Internal");

        result.ShouldMatchTypes(0);
    }

    [Fact]
    public async Task Run_WithKindFilter_ReturnsOnlyRecordTypes()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, kind: "record");

        result.ShouldMatchTypes(2, "OperationResult", "WorkItem");
        result.Types.Select(static type => type.Kind).Distinct().Is("record");
    }

    [Fact]
    public async Task Run_WithoutIncludeSummary_ReturnsSummariesByDefault()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, kind: "class");

        result.Error.IsNull();
        result.Types.Single(static type => type.DisplayName == "Documentation").Summary
            .Is("Documentation service for testing XML comment parsing. Provides summary, returns, and parameter documentation.");
    }

    [Fact]
    public async Task Run_WithoutIncludeMembers_OmitsMembersByDefault()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, kind: "class");

        result.Error.IsNull();
        result.Types.Single(static type => type.DisplayName == "Documentation").Members.IsNull();
    }

    [Fact]
    public async Task Run_WithIncludeSummaryFalse_KeepsSummariesOmitted()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, kind: "class", includeSummary: false);

        result.Error.IsNull();
        result.Types.Single(static type => type.DisplayName == "Documentation").Summary.IsNull();
    }

    [Fact]
    public async Task Run_WithIncludeMembersFalse_KeepsMembersOmitted()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, kind: "class", includeMembers: false);

        result.Error.IsNull();
        result.Types.Single(static type => type.DisplayName == "Documentation").Members.IsNull();
    }

    [Fact]
    public async Task Run_WithIncludeSummary_ReturnsTypeSummaryOnlyWhenAvailable()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, kind: "class", includeSummary: true);

        result.Error.IsNull();
        result.Types.Single(static type => type.DisplayName == "Documentation").Summary
            .Is("Documentation service for testing XML comment parsing. Provides summary, returns, and parameter documentation.");
        result.Types.Single(static type => type.DisplayName == "BaseClass").Summary.IsNull();
    }

    [Fact]
    public async Task Run_WithIncludeMembers_ReturnsLightweightDeclaredMembers()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, kind: "class", includeMembers: true);

        result.Error.IsNull();
        result.Types.Single(static type => type.DisplayName == "Documentation").Members?.Count.Is(10);
    }

    [Fact]
    public async Task Run_WithAccessibilityFilter_ReturnsNoInternalTypes()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectPath: project.FilePath, accessibility: "internal");

        result.ShouldMatchTypes(0);
    }

    [Fact]
    public async Task Run_WithLimitAndOffset_PaginatesDeterministically()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, limit: 4, offset: 5);

        result.ShouldMatchTypes(15, "StepEventArgs", "WorkerA", "WorkerB", "IFactory<T>");
    }

    [Fact]
    public async Task Run_WithInvalidKind_ReturnsValidationError()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, kind: "delegate");

        result.ShouldHaveError("invalid_input", "kind must be one of: class, record, interface, enum, or struct.");
        result.TotalCount.Is(0);
        result.Types.Count.Is(0);
    }

    [Fact]
    public async Task Run_WithInvalidAccessibility_ReturnsValidationError()
    {
        var project = Context.GetProject("ProjectCore");
        var result = await Sut.Run(CancellationToken.None, projectName: project.Name, accessibility: "package");

        result.ShouldHaveError("invalid_input", "accessibility must be one of: public, internal, protected, private, protected_internal, or private_protected.");
        result.TotalCount.Is(0);
        result.Types.Count.Is(0);
    }

    [Fact]
    public async Task Run_WhenNoProjectSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.Run(CancellationToken.None);

        result.ShouldHaveError("invalid_input", "A project selector is required. Provide projectPath, projectName, or projectId.");
        result.TotalCount.Is(0);
        result.Types.Count.Is(0);
    }

    [Fact]
    public async Task Run_WithUnknownProjectId_ReturnsValidationError()
    {
        var result = await Sut.Run(CancellationToken.None, projectId: "00000000-0000-0000-0000-000000000000");

        result.ShouldHaveError("invalid_input", "projectId did not match any project in the active workspace snapshot.");
        result.TotalCount.Is(0);
        result.Types.Count.Is(0);
        result.Error!.Details!["projectIdScope"].Is("snapshot-local");
    }
}

public sealed class ListTypesToolIsolatedTests(ITestOutputHelper output)
    : IsolatedToolTests<RoslynMcp.Tools.Inspection.ListTypes.Tool>(output)
{
    [Fact]
    public async Task Run_WithGeneratedOnlyProject_FallsBackToGeneratedSourceTypes()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var loadSolution = context.GetRequiredService<RoslynMcp.Tools.Inspection.LoadSolution.Tool>();
        var projectFilePath = Path.Combine(context.TestSolutionDirectory, "ProjectApp", "ProjectApp.csproj");

        await File.WriteAllTextAsync(projectFilePath, """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="obj\Debug\net10.0\GeneratedExecutionHooks.g.cs" />
    <ProjectReference Include="..\ProjectCore\ProjectCore.csproj" />
  </ItemGroup>

</Project>
""", CancellationToken.None);

        var load = await loadSolution.Run(CancellationToken.None, context.SolutionPath);

        load.Error.IsNull();

        var result = await sut.Run(CancellationToken.None, projectName: "ProjectApp");

        result.Error.IsNull();
        result.TotalCount.Is(1);
        result.Types.Select(static type => type.DisplayName).Is("GeneratedExecutionHooks");
        result.Context.SourceBias.Is("generated");
        result.Context.Completeness.Is("partial");
        result.Context.Limitations.Any(static limitation => limitation.Contains("Only generated declarations", StringComparison.Ordinal)).IsTrue();
    }

    [Fact]
    public async Task Run_WithMissingGeneratedArtifacts_ReportsDegradedDiscovery()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var loadSolution = context.GetRequiredService<RoslynMcp.Tools.Inspection.LoadSolution.Tool>();
        var projectFilePath = Path.Combine(context.TestSolutionDirectory, "ProjectApp", "ProjectApp.csproj");
        var generatedPath = Path.Combine(context.TestSolutionDirectory, "ProjectApp", "obj", "Debug", "net10.0", "GeneratedExecutionHooks.g.cs");

        await File.WriteAllTextAsync(projectFilePath, """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="obj\Debug\net10.0\GeneratedExecutionHooks.g.cs" />
    <ProjectReference Include="..\ProjectCore\ProjectCore.csproj" />
  </ItemGroup>

</Project>
""", CancellationToken.None);
        File.Delete(generatedPath);

        var load = await loadSolution.Run(CancellationToken.None, context.SolutionPath);

        load.Error.IsNull();
        load.Readiness.State.Is("degraded_missing_artifacts");

        var result = await sut.Run(CancellationToken.None, projectName: "ProjectApp");

        result.Error.IsNull();
        result.TotalCount.Is(0);
        result.Context.SourceBias.Is("generated");
        result.Context.Completeness.Is("degraded");
        result.Context.DegradedReasons.IsContaining("missing_artifacts");
        result.Context.RecommendedNextStep.IsNotNull();
    }
}

file static class AssertionExtensions
{
    internal static void ShouldMatchTypes(this RoslynMcp.Tools.Inspection.ListTypes.Result result, int expectedTotalCount, params string[] expectedDisplayNames)
    {
        result.Error.IsNull();
        result.TotalCount.Is(expectedTotalCount);
        result.Types.Select(static type => type.DisplayName).Is(expectedDisplayNames);
        result.Types.Select(static type => type.SymbolId).ToList().ForEach(static symbolId => symbolId.ShouldNotBeEmpty());
    }

    internal static void ShouldHaveError(this RoslynMcp.Tools.Inspection.ListTypes.Result result, string expectedCode, string expectedMessage)
    {
        result.Error.IsNotNull();
        result.Error!.Code.Is(expectedCode);
        result.Error.Message.Is(expectedMessage);
    }
}
