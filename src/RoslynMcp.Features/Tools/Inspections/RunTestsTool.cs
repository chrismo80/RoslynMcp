using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Features.Tools.Inspections;

public sealed class RunTestsTool(ITestInspectionService testInspectionService) : Tool
{
    private readonly ITestInspectionService _testInspectionService = testInspectionService ?? throw new ArgumentNullException(nameof(testInspectionService));

    [McpServerTool(Name = "run_tests", Title = "Run Tests", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to run all tests.")]
    public Task<RunTestsResult> ExecuteAsync(
        CancellationToken cancellationToken,
        [Description("Optional execution target. Omit to run the currently loaded solution. Supports solution-relative or absolute .sln, .slnx, .csproj, or directory paths when the resolved target stays within the loaded solution directory.")]
        string? target = null,
        [Description("Optional dotnet test filter expression. Passed through to --filter semantics where practical.")]
        string? filter = null)
        => _testInspectionService.RunTestsAsync(target.ToRunTestsRequest(filter), cancellationToken);
}
