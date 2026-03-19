using System.Reflection;
using Is.Assertions;
using RoslynMcp.Infrastructure;
using Xunit;

namespace RoslynMcp.Features.Tests.Inspections.Testing;

public sealed class TestInspectionServiceTests
{
    [Fact]
    public void ResolveTarget_AcceptsSolutionRootRelativeDot()
    {
        using var sandbox = new TargetResolutionSandbox();

        var resolution = sandbox.ResolveTarget(".");

        GetError(resolution).IsNull();
        GetEffectiveTargetPath(resolution).Is(sandbox.SolutionRoot);
    }

    [Fact]
    public void ResolveTarget_AcceptsAbsoluteSolutionRootPath()
    {
        using var sandbox = new TargetResolutionSandbox();

        var resolution = sandbox.ResolveTarget(sandbox.SolutionRoot);

        GetError(resolution).IsNull();
        GetEffectiveTargetPath(resolution).Is(sandbox.SolutionRoot);
    }

    [Fact]
    public void ResolveTarget_AcceptsChildPathWithinSolutionRoot()
    {
        using var sandbox = new TargetResolutionSandbox();

        var resolution = sandbox.ResolveTarget(sandbox.ChildProjectPath);

        GetError(resolution).IsNull();
        GetEffectiveTargetPath(resolution).Is(sandbox.ChildProjectPath);
    }

    [Fact]
    public void ResolveTarget_RejectsSiblingPathOutsideSolutionRoot()
    {
        using var sandbox = new TargetResolutionSandbox();

        var resolution = sandbox.ResolveTarget(sandbox.SiblingProjectPath);

        GetErrorCode(resolution).Is("invalid_input");
    }

    [Fact]
    public void IsPathWithinRoot_UsesPlatformAppropriateCaseSensitivity()
    {
        var method = GetServiceType().GetMethod("IsPathWithinRoot", BindingFlags.Static | BindingFlags.NonPublic)!;

        var baseDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcpCaseSensitivity");
        var rootDirectory = Path.Combine(baseDirectory, "ActualRoot");
        var pathWithDifferentCase = Path.Combine(baseDirectory, "actualroot", "project.csproj");

        var result = (bool)method.Invoke(null, [rootDirectory, pathWithDifferentCase])!;

        result.Is(OperatingSystem.IsWindows());
    }

    private static Type GetServiceType()
        => typeof(InfrastructureExtensions).Assembly.GetType("RoslynMcp.Infrastructure.Testing.TestInspectionService", throwOnError: true)!;

    private static object? GetError(object resolution)
        => resolution.GetType().GetProperty("Error", BindingFlags.Instance | BindingFlags.Public)!.GetValue(resolution);

    private static string? GetErrorCode(object resolution)
    {
        var error = GetError(resolution);
        return error?.GetType().GetProperty("Code", BindingFlags.Instance | BindingFlags.Public)!.GetValue(error) as string;
    }

    private static string? GetEffectiveTargetPath(object resolution)
        => resolution.GetType().GetProperty("EffectiveTargetPath", BindingFlags.Instance | BindingFlags.Public)!.GetValue(resolution) as string;

    private sealed class TargetResolutionSandbox : IDisposable
    {
        private readonly string _baseDirectory;

        public TargetResolutionSandbox()
        {
            _baseDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcp", "TestInspectionServiceTests", Guid.NewGuid().ToString("N"));
            SolutionRoot = Path.Combine(_baseDirectory, "workspace");
            SolutionPath = Path.Combine(SolutionRoot, "RoslynMcp.sln");
            ChildProjectPath = Path.Combine(SolutionRoot, "src", "Child", "Child.csproj");
            SiblingProjectPath = Path.Combine(_baseDirectory, "sibling", "Sibling.csproj");

            Directory.CreateDirectory(Path.GetDirectoryName(ChildProjectPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(SiblingProjectPath)!);
            File.WriteAllText(SolutionPath, string.Empty);
            File.WriteAllText(ChildProjectPath, string.Empty);
            File.WriteAllText(SiblingProjectPath, string.Empty);
        }

        public string SolutionRoot { get; }

        public string SolutionPath { get; }

        public string ChildProjectPath { get; }

        public string SiblingProjectPath { get; }

        public object ResolveTarget(string requestedTarget)
        {
            var method = GetServiceType().GetMethod("ResolveTarget", BindingFlags.Static | BindingFlags.NonPublic)!;
            return method.Invoke(null, [SolutionPath, requestedTarget])!;
        }

        public void Dispose()
        {
            if (Directory.Exists(_baseDirectory))
            {
                Directory.Delete(_baseDirectory, recursive: true);
            }
        }
    }
}
