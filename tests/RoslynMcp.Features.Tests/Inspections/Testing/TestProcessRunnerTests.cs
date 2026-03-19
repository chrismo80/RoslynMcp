using System.Reflection;
using Is.Assertions;
using RoslynMcp.Infrastructure;
using Xunit;

namespace RoslynMcp.Features.Tests.Inspections.Testing;

public sealed class TestProcessRunnerTests
{
    [Fact]
    public void GetWorkingDirectory_UsesDirectoryTargetItself()
    {
        using var sandbox = new WorkingDirectorySandbox();

        var workingDirectory = sandbox.GetWorkingDirectory(sandbox.DirectoryTargetPath);

        workingDirectory.Is(sandbox.DirectoryTargetPath);
    }

    [Fact]
    public void GetWorkingDirectory_UsesContainingDirectoryForFileTarget()
    {
        using var sandbox = new WorkingDirectorySandbox();

        var workingDirectory = sandbox.GetWorkingDirectory(sandbox.FileTargetPath);

        workingDirectory.Is(Path.GetDirectoryName(sandbox.FileTargetPath));
    }

    private static Type GetRunnerType()
        => typeof(InfrastructureExtensions).Assembly.GetType("RoslynMcp.Infrastructure.Testing.TestProcessRunner", throwOnError: true)!;

    private sealed class WorkingDirectorySandbox : IDisposable
    {
        private readonly string _baseDirectory;

        public WorkingDirectorySandbox()
        {
            _baseDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcp", "TestProcessRunnerTests", Guid.NewGuid().ToString("N"));
            DirectoryTargetPath = Path.Combine(_baseDirectory, "target-directory");
            FileTargetPath = Path.Combine(_baseDirectory, "target-file", "Project.csproj");

            Directory.CreateDirectory(DirectoryTargetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(FileTargetPath)!);
            File.WriteAllText(FileTargetPath, string.Empty);
        }

        public string DirectoryTargetPath { get; }

        public string FileTargetPath { get; }

        public string GetWorkingDirectory(string targetPath)
        {
            var method = GetRunnerType().GetMethod("GetWorkingDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
            return (string)method.Invoke(null, [targetPath])!;
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
