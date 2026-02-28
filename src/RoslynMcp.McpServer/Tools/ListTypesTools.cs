using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class ListTypesTools
{
    private readonly ICodeUnderstandingService _codeUnderstandingService;

    public ListTypesTools(ICodeUnderstandingService codeUnderstandingService)
    {
        _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));
    }

    [McpServerTool(Name = "list_types", Title = "List Types", ReadOnly = true, Idempotent = true)]
    [Description("Lists all source-declared types (classes, interfaces, enums, structs, records) in a project. Returns stable symbolIds and declaration locations for drill-down. Requires projectPath, projectName, or projectId from load_solution output. Supports filtering by namespace, kind (class/interface/enum/struct/record), and accessibility.")]
    public Task<ListTypesResult> ListTypesAsync(
        CancellationToken cancellationToken,
        [Description("Project selector option 1: exact project path from load_solution output. Provide exactly one selector (path, name, or id).")]
        string? projectPath = null,
        [Description("Project selector option 2: project name from load_solution output.")]
        string? projectName = null,
        [Description("Project selector option 3: projectId from load_solution output.")]
        string? projectId = null,
        [Description("Optional namespace prefix filter.")]
        string? namespacePrefix = null,
        [Description("Optional kind filter: class, record, interface, enum, or struct.")]
        string? kind = null,
        [Description("Optional accessibility filter: public, internal, protected, private, protected_internal, or private_protected.")]
        string? accessibility = null,
        [Description("Maximum results to return. Defaults to 100; clamped to 0..500.")]
        int? limit = null,
        [Description("Zero-based pagination offset. Defaults to 0.")]
        int? offset = null)
        => _codeUnderstandingService.ListTypesAsync(
            ToolContractMapper.ToListTypesRequest(projectPath, projectName, projectId, namespacePrefix, kind, accessibility, limit, offset),
            cancellationToken);
}
