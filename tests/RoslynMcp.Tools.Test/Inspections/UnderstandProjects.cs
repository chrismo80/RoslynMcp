using Is.Assertions;
using RoslynMcp.Tools.Inspection.UnderstandProjects;

namespace RoslynMcp.Tools.Test.Inspections;

public class UnderStandProjects : LoadedSolutionTests<UnderstandProjectsTool>
{
	[Fact]
	public async Task Deep()
	{
		var result = await Sut.Execute(CancellationToken.None, "deep");

		result.Projects.Count.Is(7);
	}
}