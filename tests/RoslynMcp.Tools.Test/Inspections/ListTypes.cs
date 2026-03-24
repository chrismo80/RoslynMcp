using Is.Assertions;
using RoslynMcp.Tools.Inspection.ListTypes;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

public class ListTypes(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath_WithProjectFile()
	{
		var result = await Sut.Execute(CancellationToken.None, Path.Combine("ProjectCore", "ProjectCore.csproj"));

		result.Types.Count.Is(16);

		foreach (var type in result.Types)
		{
			o.WriteLine(type.ToString());
			
			foreach(var member in type.Members)
				o.WriteLine(member.ToString());
		}
	}
	
	[Fact]
	public async Task HappyPath_WithProjectName()
	{
		var result = await Sut.Execute(CancellationToken.None, "ProjectCore");

		result.Types.Count.Is(16);
	}
}