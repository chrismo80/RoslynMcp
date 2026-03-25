using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.LoadSolution;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Test;

public abstract class SandboxTests<T> : Tests<T>, IDisposable where T : notnull
{
	private string SandBoxRoot { get; }
	
	protected SandboxTests()
	{
		SandBoxRoot = Path.Combine(Path.GetTempPath(), "RoslynMcp.Tools.Test", Guid.NewGuid().ToString("N"));

		CopyDirectory(TestSolutionDirectory, SandBoxRoot);
		
		SetWorkspaceDirectory(SandBoxRoot);
		
		LoadSolution();
	}
	
	public void Dispose()
	{
		if (!Directory.Exists(SandBoxRoot))
			return;

		foreach (var filePath in Directory.EnumerateFiles(SandBoxRoot, "*", SearchOption.AllDirectories))
			File.SetAttributes(filePath, FileAttributes.Normal);

		Directory.Delete(SandBoxRoot, recursive: true);
	}

	private static void CopyDirectory(string sourceDirectory, string targetDirectory)
	{
		Directory.CreateDirectory(targetDirectory);

		foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			var relativePath = Path.GetRelativePath(sourceDirectory, directoryPath);
            
			Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
		}

		foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
			var targetPath = Path.Combine(targetDirectory, relativePath);
			var targetParent = Path.GetDirectoryName(targetPath);

			if (!string.IsNullOrEmpty(targetParent))
				Directory.CreateDirectory(targetParent);

			File.Copy(filePath, targetPath, overwrite: false);
		}
	}
}

public abstract class LoadedSolutionTests<T> : Tests<T> where T : notnull
{
	protected LoadedSolutionTests() => LoadSolution();
}

public abstract class Tests<T> where T : notnull
{
	protected string WorkspaceDirectory { get; set; }
	
	protected string TestSolutionDirectory { get; set; }
	
	protected ServiceProvider ServiceProvider { get; }
	
	public T Sut { get; }

	protected Tests()
	{
		ServiceProvider = new ServiceCollection()
			.WithRoslynMcp()
			.BuildServiceProvider();
		
		Sut = ServiceProvider.GetRequiredService<T>();

		TestSolutionDirectory = FindTestSolutionDirectory();
		
		SetWorkspaceDirectory(TestSolutionDirectory);
	}

	protected void SetWorkspaceDirectory(string directory)
	{
		ServiceProvider.GetRequiredService<WorkspaceManager>()
			.SetWorkspaceDirectory(directory);

		WorkspaceDirectory = directory;
	}
	
	protected void LoadSolution()
	{
		ServiceProvider.GetRequiredService<McpTool>()
			.Execute(CancellationToken.None).Wait();
	}

	private static string FindTestSolutionDirectory()
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
}