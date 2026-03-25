using Is.Assertions;
using RoslynMcp.Tools.Mutation.FormatDocument;

namespace RoslynMcp.Tools.Test.Mutations;

public class FormatDocument : SandboxTests<McpTool>
{
	[Fact]
	public async Task HappyPath_DocuemntChanged()
	{
		var file = Path.Combine("ProjectImpl", "FormattingFixture.cs");
		
		var before = await File.ReadAllTextAsync(Path.Combine(WorkspaceDirectory, file));

		before.IsContaining("public int Add( int left,int right )");
		before.IsContaining("return left+right ;");
		
		var result = await Sut.Execute(CancellationToken.None, file);
		
		var after = await File.ReadAllTextAsync(Path.Combine(WorkspaceDirectory, file));

		after.IsContaining("public int Add(int left, int right)");
		after.IsContaining("return left + right;");
	}
}