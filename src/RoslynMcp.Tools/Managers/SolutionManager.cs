using System.Collections.Concurrent;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcp.Tools.Managers;

public sealed class SolutionManager : IAsyncDisposable
{
    private record Session(MSBuildWorkspace Workspace, Solution Solution, int Version);

    private readonly ConcurrentQueue<Session> _states = new();
    
    public string WorkspaceDirectory { get; set; } = Path.GetFullPath(Directory.GetCurrentDirectory());

    public Solution? Solution => _states.LastOrDefault()?.Solution;

    static SolutionManager()
    {
        MSBuildLocator.RegisterDefaults();
    }
    
    internal async Task Load(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var msBuildWorkspace = MSBuildWorkspace.Create();
        
        var solution = await msBuildWorkspace
            .OpenSolutionAsync(path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        
        Update(msBuildWorkspace, solution);
    }

    public ValueTask DisposeAsync()
    {
        Solution?.Workspace.Dispose();
        
        return ValueTask.CompletedTask;
    }

    internal void ApplyChanges(Solution solution)
    {
        if (Solution?.Workspace.TryApplyChanges(solution) == true)
            Update(_states.Last().Workspace, solution);
    }

    internal async Task<bool> Reload(CancellationToken cancellationToken)
    {
        if (Solution is null)
            return false;
        
        await Load(Solution!.FilePath!, cancellationToken);

        return true;
    }

    internal bool TryApplyChanges(Solution solution)
    {
        return Solution!.Workspace.TryApplyChanges(solution);
    }

    private void Update(MSBuildWorkspace workspace, Solution solution)
    {
        if (_states.IsEmpty)
        {
            _states.Enqueue(new Session(workspace, solution, 0));
        }
        else
        {
            _states.Enqueue(new Session(workspace, solution, _states.Last().Version + 1));
            _states.TryDequeue(out _);
        }
    }
}