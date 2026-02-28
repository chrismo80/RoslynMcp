using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class ListMembersTools
{
    private readonly ICodeUnderstandingService _codeUnderstandingService;

    public ListMembersTools(ICodeUnderstandingService codeUnderstandingService)
    {
        _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));
    }

    [McpServerTool(Name = "list_members", Title = "List Members", ReadOnly = true, Idempotent = true)]
    [Description("Lists members (methods, properties, fields, events, constructors) of a resolved type. Requires either typeSymbolId from list_types OR path+line+column pointing to a type. Supports filtering by kind, accessibility, binding (static/instance), and includes inherited members option. Returns stable symbolIds and signatures.")]
    public Task<ListMembersResult> ListMembersAsync(
        CancellationToken cancellationToken,
        [Description("Type selector mode A: typeSymbolId from list_types. Use this, or provide path+line+column.")]
        string? typeSymbolId = null,
        [Description("Type selector mode B: source file path used with line+column.")]
        string? path = null,
        [Description("Type selector mode B: 1-based line number used with path+column.")]
        int? line = null,
        [Description("Type selector mode B: 1-based column number used with path+line.")]
        int? column = null,
        [Description("Optional kind filter: method, property, field, event, or ctor.")]
        string? kind = null,
        [Description("Optional accessibility filter: public, internal, protected, private, protected_internal, or private_protected.")]
        string? accessibility = null,
        [Description("Optional binding filter: instance or static.")]
        string? binding = null,
        [Description("Include inherited members when true. Defaults to false.")]
        bool? includeInherited = null,
        [Description("Maximum results to return. Defaults to 100; clamped to 0..500.")]
        int? limit = null,
        [Description("Zero-based pagination offset. Defaults to 0.")]
        int? offset = null)
        => _codeUnderstandingService.ListMembersAsync(
            ToolContractMapper.ToListMembersRequest(typeSymbolId, path, line, column, kind, accessibility, binding, includeInherited, limit, offset),
            cancellationToken);
}
