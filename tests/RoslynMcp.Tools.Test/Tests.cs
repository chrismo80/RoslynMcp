using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Test;

public abstract class Tests<T> where T : notnull
{
	protected string TestSolutionDirectory { get; }

	protected ServiceProvider ServiceProvider { get; }

	public T Sut { get; }

	protected Tests(bool load = true)
	{
		TestSolutionDirectory = GetTestSolutionDirectory();
		ServiceProvider = CreateServiceProvider();
		Sut = ServiceProvider.GetRequiredService<T>();

		if (load)
			LoadTestSolution();
	}

	private static string GetTestSolutionDirectory()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);

		while (current is not null)
		{
			if (current.Name == "tests")
				return Path.Combine(current.FullName, "TestSolution");

			current = current.Parent;
		}

		throw new DirectoryNotFoundException("Could not locate test solution.");
	}

	private static ServiceProvider CreateServiceProvider() => new ServiceCollection()
		.WithRoslynMcp()
		.BuildServiceProvider();

	private void  LoadTestSolution()
	{
		var manager = ServiceProvider.GetRequiredService<SolutionManager>();

		manager.Load(TestSolutionDirectory, CancellationToken.None).Wait();
	}
}