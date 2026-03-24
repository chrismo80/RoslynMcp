using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.ListMembers;
using Xunit.Abstractions;
using Result = RoslynMcp.Tools.Inspection.ListTypes.Result;

namespace RoslynMcp.Tools.Test.Inspections;

public class ListMembers(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath()
	{
		var id = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");

		var result = await Sut.Execute(CancellationToken.None, id);

		result.Members.Count.Is(12);
	}

	private async Task<string> GetTypeSymbolIdAsync(string projectName, string displayName)
	{
		Result result = await ServiceProvider.GetRequiredService<Inspection.ListTypes.McpTool>()
			.Execute(CancellationToken.None, projectName);

		return result.Types.Single(type => type.Type?.DisplayName == displayName).Type.Id;
	}
}