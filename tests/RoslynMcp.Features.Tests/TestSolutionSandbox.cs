namespace RoslynMcp.Features.Tests;

public sealed class TestSolutionSandbox : IDisposable
{
    private TestSolutionSandbox(string sandboxRoot, string solutionRoot)
    {
        SandboxRoot = sandboxRoot;
        SolutionRoot = solutionRoot;
        SolutionPath = Path.Combine(SolutionRoot, "TestSolution.sln");
    }

    public string SandboxRoot { get; }

    public string SolutionRoot { get; }

    public string SolutionPath { get; }

    public static TestSolutionSandbox Create(string canonicalSolutionRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSolutionRoot);

        if (!Directory.Exists(canonicalSolutionRoot))
        {
            throw new DirectoryNotFoundException($"Canonical test solution directory '{canonicalSolutionRoot}' does not exist.");
        }

        var sandboxRoot = Path.Combine(Path.GetTempPath(), "RoslynMcp.FeatureTests", Guid.NewGuid().ToString("N"));
        var solutionRoot = Path.Combine(sandboxRoot, Path.GetFileName(canonicalSolutionRoot));

        CopyDirectory(canonicalSolutionRoot, solutionRoot);

        return new TestSolutionSandbox(sandboxRoot, solutionRoot);
    }

    public void Dispose()
    {
        if (!Directory.Exists(SandboxRoot))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(SandboxRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }

        Directory.Delete(SandboxRoot, recursive: true);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directoryPath);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            var targetParent = Path.GetDirectoryName(targetPath);

            if (!string.IsNullOrEmpty(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            File.Copy(filePath, targetPath, overwrite: false);
        }
    }
}
