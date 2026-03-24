using Is.Assertions;
using RoslynMcp.Tools.Inspection.LoadSolution;

namespace RoslynMcp.Tools.Test.Inspections;

public class LoadSolution : Tests<LoadSolutionTool>
{
	[Fact]
	public async Task HappyPath_WithoutFile()
	{
		var result = await Sut.Execute(CancellationToken.None);

		result.Projects.Count.Is(7);
	}

	[Fact]
	public async Task HappyPath_WithFile()
	{
		var result = await Sut.Execute(CancellationToken.None, "TestSolution.sln");

		result.Projects.Count.Is(7);
	}
}