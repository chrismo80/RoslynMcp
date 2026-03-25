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
		o.WriteLine(result.ToJson());

		result.Types.Count.Is(16);
		
	}

	[Fact]
	public async Task HappyPath_WithProjectName()
	{
		var result = await Sut.Execute(CancellationToken.None, "ProjectImpl");
		o.WriteLine(result.ToJson());

		result.Types.Count.Is(26);
	}
}