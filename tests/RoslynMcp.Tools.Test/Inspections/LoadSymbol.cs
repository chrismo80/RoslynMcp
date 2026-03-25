using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.LoadSymbol;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

public class LoadSymbol(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath()
	{
		var typeSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");
		var memberSymbolId = await GetMemberSymbolIdAsync(typeSymbolId, "RunAsync");

		var result = await Sut.Execute(CancellationToken.None, memberSymbolId);
	}

	private async Task<string> GetTypeSymbolIdAsync(string projectName, string displayName)
	{
		var result = await ServiceProvider.GetRequiredService<Inspection.LoadProject.McpTool>()
			.Execute(CancellationToken.None, projectName);

		return result.Types.Single(type => type.Type.DisplayName == displayName).Type.Id;
	}

	private async Task<string> GetMemberSymbolIdAsync(string typeSymbolId, string displayName)
	{
		var result = await ServiceProvider.GetRequiredService<Inspection.LoadType.McpTool>()
			.Execute(CancellationToken.None, typeSymbolId);

		return result.Members.Single(member => member.DisplayName.Contains(displayName)).SymbolId;
	}
}