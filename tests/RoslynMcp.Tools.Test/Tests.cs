using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Test;

public abstract class Tests<T>
{
	private static string _testSOlutionDirectory;

    private ServiceProvider? _provider;

	public T Sut { get; }

	public string TestSolutionDirectory { get; } = _testSOlutionDirectory;

	static Tests()
	{
		_testSOlutionDirectory = GetTestSolutionDirectory();
	}

	protected virtual ServiceProvider CreateServiceProvider() => new ServiceCollection()
		.WithRoslynMcp()
		.BuildServiceProvider();

	private static string GetTestSolutionDirectory()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);

		while (current is not null)
		{
			if (current.Name == "tests")
				return Path.Combine(current.FullName, "TestSolution");

			current = current.Parent;
		}

		throw new DirectoryNotFoundException("Could not locate test solution root from AppContext.");
	}
}