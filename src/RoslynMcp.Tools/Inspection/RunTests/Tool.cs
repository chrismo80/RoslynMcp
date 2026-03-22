using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.RunTests;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "run_tests", Title = "Run Tests", ReadOnly = true, Idempotent = true)]
	[Description("Default .NET test runner. Use this instead of 'dotnet test' unless you need unsupported CLI behavior.")]
	public Task<Result> Run(
		CancellationToken cancellationToken,
		[Description("Optional execution target. Omit to run the currently loaded solution. Supports solution-relative or absolute .sln, .slnx, .csproj, or directory paths when the resolved target stays within the loaded solution directory.")]
		string? target = null,
		[Description("Optional dotnet test filter expression. Passed through to --filter semantics where practical.")]
		string? filter = null)
		=> service.RunAsync(target.ToRequest(filter), cancellationToken);
}
