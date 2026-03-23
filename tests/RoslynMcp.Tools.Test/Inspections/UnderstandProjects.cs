using Is.Assertions;
using RoslynMcp.Tools.Inspection.LoadSolution;
using RoslynMcp.Tools.Inspection.UnderstandProjects;
using RoslynMcp.Tools.Managers;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

public class UnderStandProjects(ITestOutputHelper output)
	: InspectionTests(output)
{
	[Fact]
	public async Task Test1()
	{
		var ct = new CancellationTokenSource();

		var workspaceManager = new WorkspaceManager();
		var solutionManager = new SolutionManager();
		var symbolManager = new SymbolManager();

		workspaceManager.SetWorkspaceDirectory("/Users/moldi/Documents/Repos/RoslynMcp/tests/TestSolution");

		var loadSolutionTool = new LoadSolutionTool(workspaceManager, solutionManager);

		var result1 = await loadSolutionTool.Execute(ct.Token);

		result1.Projects.Count.Is(7);

		var understandProjectsTool = new UnderstandProjectsTool(solutionManager, symbolManager);

		var result2 = await understandProjectsTool.Execute(ct.Token, "deep");

		result2.IsNotNull();

		foreach(var project in result2.Projects)
		foreach(var type in project.Types)
			Output.WriteLine(project.Name + " - " + type);
	}
}