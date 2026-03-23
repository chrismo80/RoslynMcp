using Is.Assertions;
using RoslynMcp.Tools.Inspection.LoadSolution;
using RoslynMcp.Tools.Inspection.UnderstandProjects;
using RoslynMcp.Tools.Managers;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

public class LoadSolution(ITestOutputHelper output)
	: InspectionTests(output)
{
	[Fact]
	public async Task WithoutFile()
	{
		var ct = new CancellationTokenSource();

		var workspaceManager = new WorkspaceManager();
		var solutionManager = new SolutionManager();
		var symbolManager = new SymbolManager();

		workspaceManager.SetWorkspaceDirectory("/Users/moldi/Documents/Repos/RoslynMcp/tests/TestSolution");

		var loadSolutionTool = new LoadSolutionTool(workspaceManager, solutionManager);

		var result1 = await loadSolutionTool.Execute(ct.Token);

		result1.Projects.Count.Is(7);
	}


	[Fact]
	public async Task WithFile()
	{
		var ct = new CancellationTokenSource();

		var workspaceManager = new WorkspaceManager();
		var solutionManager = new SolutionManager();
		var symbolManager = new SymbolManager();

		workspaceManager.SetWorkspaceDirectory("/Users/moldi/Documents/Repos/RoslynMcp/tests/TestSolution");

		var loadSolutionTool = new LoadSolutionTool(workspaceManager, solutionManager);

		var result1 = await loadSolutionTool.Execute(ct.Token, "TestSolution.sln");

		result1.Projects.Count.Is(7);
	}
}