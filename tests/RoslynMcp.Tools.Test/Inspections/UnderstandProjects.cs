using Is.Assertions;
using RoslynMcp.Tools.Inspection.UnderstandProjects;

namespace RoslynMcp.Tools.Test.Inspections;

public class UnderStandProjects : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath_Deep()
	{
		var result = await Sut.Execute(CancellationToken.None, "deep");

		result.Projects.Count.Is(7);

		var projects = result.Projects.Select(project => project.ProjectPath).ToArray();

		projects.IsContaining(Path.Combine("ProjectCore", "ProjectCore.csproj"));
		projects.IsContaining(Path.Combine("ProjectApp", "ProjectApp.csproj"));
		projects.IsContaining(Path.Combine("ProjectImpl", "ProjectImpl.csproj"));
	}
}