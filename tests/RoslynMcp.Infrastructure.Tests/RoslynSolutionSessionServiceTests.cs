using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Infrastructure.Workspace;
using Is.Assertions;

namespace RoslynMcp.Infrastructure.Tests;

[CollectionDefinition("RoslynSolutionSession", DisableParallelization = true)]
public sealed class RoslynSolutionSessionCollectionDefinition
{
}

[Collection("RoslynSolutionSession")]
public sealed class RoslynSolutionSessionServiceTests
{
    [Fact]
    public async Task DiscoverSolutions_ReturnsDeterministicOrdering()
    {
        var root = CreateWorkspaceRoot("z.sln", "a.sln", Path.Combine("nested", "b.sln"));
        try
        {
            var service = new RoslynSolutionSessionService();
            var result = await service.DiscoverSolutionsAsync(new DiscoverSolutionsRequest(root), CancellationToken.None);

            result.Error.IsNull();
            result.SolutionPaths.Count.Is(3);
            result.SolutionPaths.ToArray().Is(new[]
            {
                Path.Combine(root, "a.sln"),
                Path.Combine(root, "nested", "b.sln"),
                Path.Combine(root, "z.sln")
            });
        }
        finally
        {
            TryCleanup(root);
        }
    }

    [Fact]
    public async Task SelectSolution_ReturnsNotFoundForUnknownPath()
    {
        var service = new RoslynSolutionSessionService();
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.sln");

        var result = await service.SelectSolutionAsync(new SelectSolutionRequest(missingPath), CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.SolutionNotFound);
        result.SelectedSolutionPath.IsNull();
    }

    [Fact]
    public async Task SelectSolution_CanOpenRealSolutionAndReload()
    {
        var service = new RoslynSolutionSessionService();
        var solutionPath = LocateRepositorySolution();

        var selectResult = await service.SelectSolutionAsync(new SelectSolutionRequest(solutionPath), CancellationToken.None);
        selectResult.Error.IsNull();
        selectResult.SelectedSolutionPath.Is(Path.GetFullPath(solutionPath));

        var reloadResult = await service.ReloadSolutionAsync(new ReloadSolutionRequest(), CancellationToken.None);
        reloadResult.Success.IsTrue();
        reloadResult.Error.IsNull();
    }

    [Fact]
    public async Task ReloadSolution_ReturnsErrorIfNoSelection()
    {
        var service = new RoslynSolutionSessionService();

        var result = await service.ReloadSolutionAsync(new ReloadSolutionRequest(), CancellationToken.None);

        result.Error.IsNotNull();
        result.Success.IsFalse();
        result.Error?.Code.Is(ErrorCodes.SolutionNotSelected);
    }

    private static string CreateWorkspaceRoot(params string[] solutions)
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpSessionTests", Guid.NewGuid().ToString("N"));
        foreach (var relativePath in solutions)
        {
            var segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var combination = new[] { root }.Concat(segments).ToArray();
            var path = Path.Combine(combination);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "Microsoft Visual Studio Solution File, Format Version 12.00\r\n");
        }

        return root;
    }

    private static void TryCleanup(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private static string LocateRepositorySolution()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "RoslynMcp.sln");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate RoslynMcp.sln for tests.");
    }
}
