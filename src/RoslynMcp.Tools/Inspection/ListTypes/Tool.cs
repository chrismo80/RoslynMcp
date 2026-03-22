using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Inspection.ListTypes;

[McpServerToolType]
public sealed class Tool(Service service)
{
    [McpServerTool(Name = "list_types", Title = "List Types", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to list types declared in a specific loaded project. It is useful for project-scoped discovery, for finding type symbols before follow-up calls such as list_members or resolve_symbol, and for optionally enriching only the returned type entries with XML summaries or lightweight declared-member previews. For automation, prefer projectPath as the stable selector; projectId is snapshot-local to the active workspace snapshot. Results prefer handwritten declarations by default and report source bias, completeness, and degraded discovery hints.")]
    public Task<Result> Run(
        CancellationToken cancellationToken,
        [Description("Exact path to a project file (.csproj). Specify only one of projectPath, projectName, or projectId.")]
        string? projectPath = null,
        [Description("Name of a project. Specify only one of projectPath, projectName, or projectId.")]
        string? projectName = null,
        [Description("Project identifier from the current loaded workspace snapshot. projectId values are snapshot-local and can change after reload, so prefer projectPath for durable automation. Specify only one of projectPath, projectName, or projectId.")]
        string? projectId = null,
        [Description("Filter to only types in namespaces starting with this prefix.")]
        string? namespacePrefix = null,
        [Description("Filter by type kind: class, record, interface, enum, or struct.")]
        string? kind = null,
        [Description("Filter by accessibility: public, internal, protected, private, protected_internal, or private_protected.")]
        string? accessibility = null,
        [Description("When omitted or true, includes XML documentation summaries for returned type entries when available. Pass false to omit summaries.")]
        bool? includeSummary = null,
        [Description("When true, includes a lightweight preview of declared members for each returned type entry. This is not full member metadata: each member is returned as a single normalized accessibility-plus-signature string, and only members declared on that type are included. Enrichment is applied only to the returned type entries. Use list_members as the detailed follow-up tool. When omitted or false, members are omitted.")]
        bool? includeMembers = null,
        [Description("Maximum number of results to return. Defaults to 100, maximum 500.")]
        int? limit = null,
        [Description("Number of results to skip for pagination. Defaults to 0.")]
        int? offset = null)
        => service.RunAsync(projectPath.ToRequest(projectName, projectId, namespacePrefix, kind, accessibility, includeSummary, includeMembers, limit, offset), cancellationToken);
}