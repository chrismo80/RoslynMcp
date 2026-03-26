using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcp.Tools.Managers;

public sealed class SolutionManager(SymbolManager symbolManager) : Manager, IAsyncDisposable
{
    private record Session(MSBuildWorkspace Workspace, Solution Solution, int Version);

    private Session? _session;
    
    internal Solution? Solution => _session?.Solution;
    internal int? WorkspaceId => _session?.Version;

    static SolutionManager()
    {
        MSBuildLocator.RegisterDefaults();
    }
    
    internal async Task<Solution> Load(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var msBuildWorkspace = MSBuildWorkspace.Create();
        
        var solution = await msBuildWorkspace
            .OpenSolutionAsync(path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        
        Update(msBuildWorkspace, solution);
        
        return solution;
    }

    public ValueTask DisposeAsync()
    {
        Solution?.Workspace.Dispose();
        
        return ValueTask.CompletedTask;
    }
    
    internal void ApplyChanges(Solution solution)
    {
        if (Solution?.Workspace.TryApplyChanges(solution) == true)
            Update(_session!.Workspace, solution);
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
        var session = new Session(workspace, solution, (_session?.Version ?? 0) + 1);

        var old = Interlocked.Exchange(ref _session, session);
        
        old?.Workspace.Dispose();
        
        symbolManager.Clear();
    }
}