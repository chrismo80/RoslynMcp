using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class ResolveSymbolTools
{
    private readonly ICodeUnderstandingService _codeUnderstandingService;

    public ResolveSymbolTools(ICodeUnderstandingService codeUnderstandingService)
    {
        _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));
    }

    [McpServerTool(Name = "resolve_symbol", Title = "Resolve Symbol", ReadOnly = true, Idempotent = true)]
    [Description("Resolves a symbol into a canonical symbolId. Use this FIRST before explain_symbol, trace_flow, or list_members. Supports three selector modes: (A) symbolId lookup, (B) source position (path+line+column), or (C) qualifiedName (fully qualified or short name). Mode C requires project selector (path/name/id).")]
    public Task<ResolveSymbolResult> ResolveSymbolAsync(
        CancellationToken cancellationToken,
        [Description("Selector mode A: canonical symbolId lookup.")]
        string? symbolId = null,
        [Description("Selector mode B: source file path used with line+column lookup.")]
        string? path = null,
        [Description("Selector mode B: 1-based line number used with path+column.")]
        int? line = null,
        [Description("Selector mode B: 1-based column number used with path+line.")]
        int? column = null,
        [Description("Selector mode C: qualifiedName lookup (fully qualified or short name).")]
        string? qualifiedName = null,
        [Description("Optional project selector for qualifiedName lookup: exact project path from load_solution.")]
        string? projectPath = null,
        [Description("Optional project selector for qualifiedName lookup: project name from load_solution.")]
        string? projectName = null,
        [Description("Optional project selector for qualifiedName lookup: projectId from load_solution.")]
        string? projectId = null)
        => _codeUnderstandingService.ResolveSymbolAsync(
            ToolContractMapper.ToResolveSymbolRequest(symbolId, path, line, column, qualifiedName, projectPath, projectName, projectId),
            cancellationToken);
}
