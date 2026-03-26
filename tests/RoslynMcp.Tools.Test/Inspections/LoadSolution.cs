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
		o.WriteLine(result.ToJson());

		result.Projects.Count.Is(7);
	}

	[Fact]
	public async Task HappyPath_WithFile()
	{
		var result = await Sut.Execute(CancellationToken.None, "TestSolution.sln");
		o.WriteLine(result.ToJson());

		result.Error?.Message.IsEmpty();
		result.Projects.Count.Is(7);
	}
	
	[Fact]
	public async Task HappyPath_WithFolder()
	{
		SetWorkspaceDirectory(Directory.GetParent(TestSolutionDirectory)!.FullName);
	
		var result = await Sut.Execute(CancellationToken.None, "TestSolution");
		o.WriteLine(result.ToJson());

		result.Error?.Message.IsEmpty();
		result.Projects.Count.Is(7);
	}
}