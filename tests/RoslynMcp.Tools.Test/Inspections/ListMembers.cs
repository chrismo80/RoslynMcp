using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.ListMembers;
using Xunit.Abstractions;

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
	
	private async Task<string> GetTypeSymbolIdAsync(string projectName, string typeDisplayName)
	{
		var typesResult = await ServiceProvider.GetRequiredService<Inspection.ListTypes.McpTool>()
			.Execute(CancellationToken.None, projectName);

		return typesResult.Types.Single(type => type.DisplayName == typeDisplayName).SymbolId;
	}
}