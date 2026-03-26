using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.RunTests;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

public class RunTestsNoTarget(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath_NoTarget_ButSolution()
	{
		await ServiceProvider.GetRequiredService<RoslynMcp.Tools.Inspection.LoadSolution.McpTool>()
			.Execute(CancellationToken.None);

		var result = await Sut.Execute(CancellationToken.None);
		o.WriteLine(result.ToJson());

		result.Outcome.Is("test_failures");
	}

	[Fact]
	public async Task HappyPath_NoTarget_NoFilter()
	{
		var result = await Sut.Execute(CancellationToken.None);
		o.WriteLine(result.ToJson());

		result.Outcome.Is("test_failures");
	}

	[Fact]
	public async Task HappyPath_NoTarget_WithFilter()
	{
		var result = await Sut.Execute(CancellationToken.None, filter: "Passing_filter_test");
		o.WriteLine(result.ToJson());

		result.Outcome.Is("passed");
		result.Counts.Passed.Is(1);
		result.Counts.Failed.Is(0);
	}
}

public class RunTestsWithTarget(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath_WithTarget_BuildFailed()
	{
		var result = await Sut.Execute(CancellationToken.None, Path.Combine("RunTestsFixtures", "BrokenBuildTests"));
		o.WriteLine(result.ToJson());

		result.Outcome.Is("build_failed");
	}

	[Fact]
	public async Task HappyPath_WithTarget_SimpleFailure()
	{
		var result = await Sut.Execute(CancellationToken.None, Path.Combine("RunTestsFixtures", "FirstSolutionFailureTests"));
		o.WriteLine(result.ToJson());

		result.Outcome.Is("test_failures");
		result.Counts.Passed.Is(0);
		result.Counts.Failed.Is(1);
	}

	[Fact]
	public async Task HappyPath_WithTarget_MixedOutcome()
	{
		var result = await Sut.Execute(CancellationToken.None, Path.Combine("RunTestsFixtures", "MixedOutcomeTests"));
		o.WriteLine(result.ToJson());

		result.Outcome.Is("test_failures");
		result.Counts.Passed.Is(1);
		result.Counts.Failed.Is(3);
	}

	[Fact]
	public async Task HappyPath_WithProjectTarget_MixedOutcome()
	{
		var result = await Sut.Execute(CancellationToken.None, Path.Combine("RunTestsFixtures", "MixedOutcomeTests", "MixedOutcomeTests.csproj"));
		o.WriteLine(result.ToJson());

		result.Outcome.Is("test_failures");
		result.Counts.Passed.Is(1);
		result.Counts.Failed.Is(3);
	}
}