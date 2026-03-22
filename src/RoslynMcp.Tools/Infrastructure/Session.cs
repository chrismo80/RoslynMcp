using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcp.Tools.Infrastructure;

internal sealed class Session(string workspaceRoot, string selectedSolutionPath, MSBuildWorkspace workspace, Solution solution) : IDisposable
{
    public string WorkspaceRoot { get; } = workspaceRoot;
    public string SelectedSolutionPath { get; } = selectedSolutionPath;
    public MSBuildWorkspace Workspace { get; } = workspace;
    public Solution Solution { get; private set; } = solution;

    public void UpdateSolution(Solution solution)
        => Solution = solution;

    public void Dispose() => Workspace.Dispose();
}
