using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.RunTests;

[McpServerToolType]
public sealed class McpTool(WorkspaceManager workspaceManager) : Tool
{
    [McpServerTool(Name = "run_tests", Title = "Run Tests", ReadOnly = true, Idempotent = true)]
    [Description("Default .NET test runner. Use this instead of 'dotnet test' unless you need unsupported CLI behavior.")]
    public async Task<Result> Execute(
        CancellationToken cancellationToken,
        [Description("Optional execution target. Omit to run the currently loaded solution. Supports solution-relative or absolute .sln, .slnx, .csproj, or directory paths when the resolved target stays within the loaded solution directory.")]
        string? target = null,
        [Description("Optional dotnet test filter expression. Passed through to --filter semantics where practical.")]
        string? filter = null)
    {
        try
        {
            return await DotNet.Test(workspaceManager, target, filter, cancellationToken);
        }
        catch (Exception e)
        {
            return Error(e.Message, new Dictionary<string, string>
            {
                ["inner exception"] = e.InnerException?.Message ?? string.Empty,
                ["stack trace"] = e.StackTrace ?? string.Empty
            });
        }
    }
    
    private static Result Error(string message, IReadOnlyDictionary<string, string>? details = null)
    {
        return new Result(null, -99, [], [], null, new ErrorInfo(message, details));
    }
}