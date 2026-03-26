using Is.Assertions;
using RoslynMcp.Tools.Managers;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Managers;

public class Workspace(ITestOutputHelper o) : Tests<WorkspaceManager>
{
	[Fact]
	public async Task PathChecks()
	{
		Sut.ToAbsolutePath(null).IsNull();
		Sut.ToAbsolutePath("").Is(Sut.WorkspaceDirectory);
		Sut.ToAbsolutePath("ProjectCore").Is(Path.Combine(Sut.WorkspaceDirectory, "ProjectCore"));
		Sut.ToAbsolutePath("ProjectCore\\ProjectCore.csproj").Is(Path.Combine(Sut.WorkspaceDirectory, "ProjectCore", "ProjectCore.csproj"));
		Sut.ToAbsolutePath("ProjectCore/ProjectCore.csproj").Is(Path.Combine(Sut.WorkspaceDirectory, "ProjectCore", "ProjectCore.csproj"));
	}
}