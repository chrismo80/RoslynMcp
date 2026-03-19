using System.Reflection;
using Is.Assertions;
using RoslynMcp.Infrastructure;
using Xunit;

namespace RoslynMcp.Features.Tests.Inspections.Testing;

public sealed class TestInspectionServiceTests
{
    [Fact]
    public void IsPathWithinRoot_UsesPlatformAppropriateCaseSensitivity()
    {
        var serviceType = typeof(InfrastructureExtensions).Assembly.GetType("RoslynMcp.Infrastructure.Testing.TestInspectionService", throwOnError: true)!;
        var method = serviceType.GetMethod("IsPathWithinRoot", BindingFlags.Static | BindingFlags.NonPublic)!;

        var baseDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcpCaseSensitivity");
        var rootDirectory = Path.Combine(baseDirectory, "ActualRoot");
        var pathWithDifferentCase = Path.Combine(baseDirectory, "actualroot", "project.csproj");

        var result = (bool)method.Invoke(null, [rootDirectory, pathWithDifferentCase])!;

        result.Is(OperatingSystem.IsWindows());
    }
}
