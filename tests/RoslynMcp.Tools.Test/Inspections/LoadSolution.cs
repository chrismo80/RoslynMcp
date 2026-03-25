using Is.Assertions;
using RoslynMcp.Tools.Inspection.LoadSolution;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

public class LoadSolution(ITestOutputHelper o) : Tests<McpTool>
{
	[Fact]
	public async Task HappyPath_WithoutFile()
	{
		var result = await Sut.Execute(CancellationToken.None);

		result.Projects.Count.Is(7);
		
		o.WriteLine(result.ToJson());
	}

	[Fact]
	public async Task HappyPath_WithFile()
	{
		var result = await Sut.Execute(CancellationToken.None, "TestSolution.sln");

		result.Projects.Count.Is(7);
	}
}