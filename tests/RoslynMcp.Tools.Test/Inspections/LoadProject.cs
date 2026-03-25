using Is.Assertions;
using RoslynMcp.Tools.Inspection.LoadProject;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

public class LoadProject(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath_WithProjectFile()
	{
		var result = await Sut.Execute(CancellationToken.None, Path.Combine("ProjectCore", "ProjectCore.csproj"));

		result.Types.Count.Is(16);
		
		o.WriteLine(result.ToJson());
	}

	[Fact]
	public async Task HappyPath_WithProjectName()
	{
		var result = await Sut.Execute(CancellationToken.None, "ProjectCore");

		result.Types.Count.Is(16);
	}
}