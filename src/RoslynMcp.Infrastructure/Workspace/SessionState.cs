using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcp.Infrastructure.Workspace;

internal sealed class SessionState : IDisposable
{
    public SessionState(string workspaceRoot, string selectedSolutionPath, MSBuildWorkspace workspace, Solution solution)
    {
        WorkspaceRoot = workspaceRoot;
        SelectedSolutionPath = selectedSolutionPath;
        Workspace = workspace;
        Solution = solution;
    }

    public string WorkspaceRoot { get; }
    public string SelectedSolutionPath { get; }
    public MSBuildWorkspace Workspace { get; }
    public Solution Solution { get; private set; }

    public void UpdateSolution(Solution solution)
    {
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
    }

    public void Dispose()
    {
        Workspace.Dispose();
    }
}
