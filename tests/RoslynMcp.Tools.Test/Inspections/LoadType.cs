using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.LoadType;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

public class LoadType(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath()
	{
		var id = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");

		var result = await Sut.Execute(CancellationToken.None, id);
		o.WriteLine(result.ToJson());

		result.Members.Count.Is(12);
	}
	
	[Fact]
	public async Task HappyPath_Interface()
	{
		var symbolId = await GetTypeSymbolIdAsync("ProjectCore", "IOperation");

		var result = await Sut.Execute(CancellationToken.None, symbolId);
		o.WriteLine(result.ToJson());
	}
	
	private async Task<string> GetTypeSymbolIdAsync(string projectName, string displayName)
	{
		var result = await ServiceProvider.GetRequiredService<Inspection.LoadProject.McpTool>()
			.Execute(CancellationToken.None, projectName);

		return result.Types.Single(type => type.Type?.DisplayName == displayName).Type.SymbolId;
	}
}