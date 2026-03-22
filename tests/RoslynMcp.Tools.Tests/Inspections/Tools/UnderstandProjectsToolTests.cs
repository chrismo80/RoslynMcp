using Is.Assertions;
using RoslynMcp.Tools.Tests.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

[Collection(SharedSandboxCollections.AnalysisCollectionName)]
public sealed class UnderstandProjectsToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<RoslynMcp.Tools.Inspection.UnderstandProjects.Tool>(fixture, output), IClassFixture<SharedSandboxFixture>
{
    [Fact]
    public async Task Run_WithQuickProfile_ReturnsProjectRelationshipsWithoutTypesOrHotspots()
    {
        var result = await Sut.Run(CancellationToken.None, profile: "quick");

        result.Error.IsNull();
        result.Profile.Is("quick");
        result.Hotspots.Count.Is(0);

        result.Projects.Count.IsGreaterThan(0);
        result.Projects.All(static project => project.Types.Count == 0).IsTrue();

        var projectsByName = result.Projects.ToDictionary(static project => project.Name, StringComparer.Ordinal);
        projectsByName.ContainsKey("ProjectApp").IsTrue();
        projectsByName.ContainsKey("ProjectCore").IsTrue();
        projectsByName.ContainsKey("ProjectImpl").IsTrue();

        projectsByName["ProjectApp"].OutgoingDependencyProjectPaths.Is(Context.GetProject("ProjectCore").FilePath, Context.GetProject("ProjectImpl").FilePath);
        projectsByName["ProjectApp"].IncomingDependencyProjectPaths.Count.Is(0);
        projectsByName["ProjectApp"].Types.Count.Is(0);

        projectsByName["ProjectCore"].OutgoingDependencyProjectPaths.Count.Is(0);
        projectsByName["ProjectCore"].IncomingDependencyProjectPaths.Is(Context.GetProject("ProjectApp").FilePath, Context.GetProject("ProjectImpl").FilePath);
        projectsByName["ProjectCore"].Types.Count.Is(0);

        projectsByName["ProjectImpl"].OutgoingDependencyProjectPaths.Is(Context.GetProject("ProjectCore").FilePath);
        projectsByName["ProjectImpl"].IncomingDependencyProjectPaths.Is(Context.GetProject("ProjectApp").FilePath);
        projectsByName["ProjectImpl"].Types.Count.Is(0);
    }

    [Fact]
    public async Task Run_WithInvalidProfile_FallsBackToStandardAndReturnsCompactTypes()
    {
        var result = await Sut.Run(CancellationToken.None, profile: "invalid-profile");

        result.Error.IsNull();
        result.Profile.Is("standard");
        result.Hotspots.Count.Is(0);

        var projectApp = result.Projects.Single(static project => project.Name == "ProjectApp");
        projectApp.Types.Count.Is(3);
        projectApp.Types.All(static type => type.Contains(": ", StringComparison.Ordinal)).IsTrue();
        projectApp.Types.Is(
            MatchCompactType("ProjectApp.AppEntryPoints", projectApp.Types),
            MatchCompactType("ProjectApp.AppOrchestrator", projectApp.Types),
            MatchCompactType("ProjectApp.MethodMutationTestTarget", projectApp.Types));
    }

    [Fact]
    public async Task Run_WithDeepProfile_ReturnsTypesAndTenHotspots()
    {
        var result = await Sut.Run(CancellationToken.None, profile: "deep");

        result.Error.IsNull();
        result.Profile.Is("deep");
        result.Projects.All(static project => project.Types.Count > 0).IsTrue();
        result.Hotspots.Count.Is(10);
        result.Hotspots.Select(static hotspot => hotspot.SymbolId).ToList().ForEach(static symbolId => symbolId.ShouldNotBeEmpty());
        result.Hotspots.All(static hotspot => !hotspot.Location!.FilePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) && !hotspot.Location.FilePath.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    private static string MatchCompactType(string qualifiedTypeName, IReadOnlyList<string> types)
        => types.Single(type => type.EndsWith(qualifiedTypeName, StringComparison.Ordinal));
}
