using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Mutation.FormatDocument;

[McpServerToolType]
public sealed class Tool(Service service)
{
	[McpServerTool(Name = "format_document", Title = "Format Document")]
	[Description("Use this tool when you need to format exactly one C# source document in the loaded solution using the solution's current formatting and style settings. Returns whether formatting changes were applied and persisted.")]
	public Task<Result> Run(CancellationToken cancellationToken,
		[Description("The path to the C# source file to format. The file must be part of the currently loaded solution.")]
		string path)
		=> service.RunAsync(path.ToRequest(), cancellationToken);
}
