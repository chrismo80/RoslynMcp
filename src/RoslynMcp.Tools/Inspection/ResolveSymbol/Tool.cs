using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.ResolveSymbol;

[McpServerToolType]
public sealed class Tool(Service service)
{
    [McpServerTool(Name = "resolve_symbol", Title = "Resolve Symbol", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you have a source position (file path + line + column), a qualified symbol name, or an existing symbolId and need the stable symbolId and declaration location used by other navigation tools. Qualified-name lookup can search the whole loaded solution, but projectPath is the preferred stable disambiguator for automation.")]
    public Task<Result> Run(
        CancellationToken cancellationToken,
        [Description("An existing symbol ID to look up. Provide this OR path+line+column OR qualifiedName.")]
        string? symbolId = null,
        [Description("Path to a source file. Provide this together with line and column instead of symbolId or qualifiedName.")]
        string? path = null,
        [Description("Line number (1-based) in the source file.")]
        int? line = null,
        [Description("Column number (1-based) in the source file.")]
        int? column = null,
        [Description("A fully qualified or short type/member name (e.g., System.String, MyNamespace.MyType.MyMethod, or MyMethod). Provide this instead of symbolId or path+line+column.")]
        string? qualifiedName = null,
        [Description("Optional project scope for qualifiedName lookup — path to a project that contains the symbol. Use to narrow ambiguous matches.")]
        string? projectPath = null,
        [Description("Optional project scope for qualifiedName lookup — name of a project that contains the symbol. Use to narrow ambiguous matches.")]
        string? projectName = null,
        [Description("Optional project scope for qualifiedName lookup — project ID from the active workspace snapshot. projectId values are snapshot-local and can change after reload, so prefer projectPath when you need a durable selector.")]
        string? projectId = null)
        => service.RunAsync(symbolId.ToRequest(path, line, column, qualifiedName, projectPath, projectName, projectId), cancellationToken);
}
