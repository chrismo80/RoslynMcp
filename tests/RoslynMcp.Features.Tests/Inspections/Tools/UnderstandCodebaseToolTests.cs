using Is.Assertions;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class UnderstandCodebaseToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<UnderstandCodebaseTool>(fixture, output)
{
    [Fact]
    public async Task UnderstandCodebaseAsync_WithQuickProfile_ReturnsOverviewAndHotspots()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, profile: "quick");

        result.Error.ShouldBeNone();
        result.Profile.Is("quick");
        result.Modules.Select(static module => module.Name).Is("ProjectApp", "ProjectCore", "ProjectImpl");
        result.Hotspots.Count.Is(3);
    }

    [Fact]
    public async Task UnderstandCodebaseAsync_WithInvalidProfile_FallsBackToStandardProfile()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, profile: "invalid-profile");

        result.Error.ShouldBeNone();
        result.Profile.Is("standard");
    }

    [Fact]
    public async Task UnderstandCodebaseAsync_ReturnsExpectedModuleDependencyShape()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, profile: "standard");

        result.Error.ShouldBeNone();
        result.Modules.Select(static module => $"{module.Name}:{module.OutgoingDependencies}:{module.IncomingDependencies}").Is("ProjectApp:2:0", "ProjectCore:0:2", "ProjectImpl:1:1");
        result.Hotspots.All(static hotspot => !string.IsNullOrWhiteSpace(hotspot.DisplayName) && !string.IsNullOrWhiteSpace(hotspot.FilePath) && hotspot.Score >= 0) .IsTrue();
    }
}