using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Analysis;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;

namespace RoslynMcp.McpServer;

internal static class ToolContractMapper
{
    private const int MinimumDepth = 1;
    private const int MaximumOutlineDepth = 3;
    private const int MaximumCallGraphDepth = 4;
    private const int DefaultMaxDerived = 200;
    private const int MinimumLineOrColumn = 1;

    public static DiscoverSolutionsRequest ToDiscoverSolutionsRequest(string? workspaceRoot)
        => new(NormalizeString(workspaceRoot));

    public static SelectSolutionRequest ToSelectSolutionRequest(string? solutionPath)
        => new(NormalizeString(solutionPath));

    public static ReloadSolutionRequest ToReloadSolutionRequest()
        => new();

    public static FindSymbolRequest ToFindSymbolRequest(string? symbolId)
        => new(NormalizeSymbolId(symbolId));

    public static GetSymbolAtPositionRequest ToGetSymbolAtPositionRequest(string? path, int line, int column)
        => new(NormalizeString(path), NormalizePosition(line), NormalizePosition(column));

    public static SearchSymbolsRequest ToSearchSymbolsRequest(string? query, int? limit, int? offset)
        => new(NormalizeString(query), NormalizeNonNegative(limit), NormalizeNonNegative(offset));

    public static SearchSymbolsScopedRequest ToSearchSymbolsScopedRequest(
        string? query,
        string? scope,
        string? path,
        string? kind,
        string? accessibility,
        int? limit,
        int? offset)
        => new(
            NormalizeString(query),
            NormalizeScope(scope),
            NormalizeOptionalString(path),
            NormalizeOptionalString(kind),
            NormalizeOptionalString(accessibility),
            NormalizeNonNegative(limit),
            NormalizeNonNegative(offset));

    public static GetSignatureRequest ToGetSignatureRequest(string? symbolId)
        => new(NormalizeSymbolId(symbolId));

    public static FindReferencesRequest ToFindReferencesRequest(string? symbolId)
        => new(NormalizeSymbolId(symbolId));

    public static FindReferencesScopedRequest ToFindReferencesScopedRequest(string? symbolId, string? scope, string? path)
        => new(NormalizeSymbolId(symbolId), NormalizeScope(scope), NormalizeOptionalString(path));

    public static FindImplementationsRequest ToFindImplementationsRequest(string? symbolId)
        => new(NormalizeSymbolId(symbolId));

    public static GetTypeHierarchyRequest ToGetTypeHierarchyRequest(string? symbolId, bool? includeTransitive, int? maxDerived)
        => new(NormalizeSymbolId(symbolId), includeTransitive ?? true, NormalizeNonNegative(maxDerived) ?? DefaultMaxDerived);

    public static GetSymbolOutlineRequest ToGetSymbolOutlineRequest(string? symbolId, int? depth)
        => new(NormalizeSymbolId(symbolId), NormalizeInRange(depth, MinimumDepth, MaximumOutlineDepth));

    public static GetCallersRequest ToGetCallersRequest(string? symbolId, int? maxDepth)
        => new(NormalizeSymbolId(symbolId), NormalizeDepth(maxDepth));

    public static GetCalleesRequest ToGetCalleesRequest(string? symbolId, int? maxDepth)
        => new(NormalizeSymbolId(symbolId), NormalizeDepth(maxDepth));

    public static GetCallGraphRequest ToGetCallGraphRequest(string? symbolId, string? direction, int? maxDepth)
        => new(NormalizeSymbolId(symbolId), NormalizeDirection(direction), NormalizeInRange(maxDepth, MinimumDepth, MaximumCallGraphDepth));

    public static AnalyzeSolutionRequest ToAnalyzeSolutionRequest()
        => new();

    public static AnalyzeScopeRequest ToAnalyzeScopeRequest(string? scope, string? path)
        => new(NormalizeScope(scope), NormalizeOptionalString(path));

    public static GetCodeMetricsRequest ToGetCodeMetricsRequest()
        => new();

    public static LoadSolutionRequest ToLoadSolutionRequest(string? solutionHintPath)
        => new(NormalizeOptionalString(solutionHintPath));

    public static UnderstandCodebaseRequest ToUnderstandCodebaseRequest(string? profile)
        => new(NormalizeOptionalString(profile));

    public static ExplainSymbolRequest ToExplainSymbolRequest(string? symbolId, string? path, int? line, int? column)
        => new(
            NormalizeOptionalString(symbolId),
            NormalizeOptionalString(path),
            line.HasValue ? NormalizePosition(line.Value) : null,
            column.HasValue ? NormalizePosition(column.Value) : null);

    public static ListTypesRequest ToListTypesRequest(
        string? projectPath,
        string? projectName,
        string? projectId,
        string? namespacePrefix,
        string? kind,
        string? accessibility,
        int? limit,
        int? offset)
        => new(
            NormalizeOptionalString(projectPath),
            NormalizeOptionalString(projectName),
            NormalizeOptionalString(projectId),
            NormalizeOptionalString(namespacePrefix),
            NormalizeOptionalString(kind)?.ToLowerInvariant(),
            NormalizeOptionalString(accessibility)?.ToLowerInvariant(),
            NormalizeNonNegative(limit),
            NormalizeNonNegative(offset));

    public static ListMembersRequest ToListMembersRequest(
        string? typeSymbolId,
        string? path,
        int? line,
        int? column,
        string? kind,
        string? accessibility,
        string? binding,
        bool? includeInherited,
        int? limit,
        int? offset)
        => new(
            NormalizeOptionalString(typeSymbolId),
            NormalizeOptionalString(path),
            line.HasValue ? NormalizePosition(line.Value) : null,
            column.HasValue ? NormalizePosition(column.Value) : null,
            NormalizeOptionalString(kind)?.ToLowerInvariant(),
            NormalizeOptionalString(accessibility)?.ToLowerInvariant(),
            NormalizeOptionalString(binding)?.ToLowerInvariant(),
            includeInherited ?? false,
            NormalizeNonNegative(limit),
            NormalizeNonNegative(offset));

    public static ResolveSymbolRequest ToResolveSymbolRequest(
        string? symbolId,
        string? path,
        int? line,
        int? column,
        string? qualifiedName,
        string? projectPath,
        string? projectName,
        string? projectId)
        => new(
            NormalizeOptionalString(symbolId),
            NormalizeOptionalString(path),
            line.HasValue ? NormalizePosition(line.Value) : null,
            column.HasValue ? NormalizePosition(column.Value) : null,
            NormalizeOptionalString(qualifiedName),
            NormalizeOptionalString(projectPath),
            NormalizeOptionalString(projectName),
            NormalizeOptionalString(projectId));

    public static TraceFlowRequest ToTraceFlowRequest(string? symbolId, string? path, int? line, int? column, string? direction, int? depth)
        => new(
            NormalizeOptionalString(symbolId),
            NormalizeOptionalString(path),
            line.HasValue ? NormalizePosition(line.Value) : null,
            column.HasValue ? NormalizePosition(column.Value) : null,
            NormalizeOptionalString(direction)?.ToLowerInvariant(),
            NormalizeNonNegative(depth));

    public static FindCodeSmellsRequest ToFindCodeSmellsRequest(
        string? path)
        => new(
            NormalizeString(path));

    private static int NormalizeDepth(int? value)
        => Math.Max(value ?? MinimumDepth, MinimumDepth);

    private static int NormalizePosition(int value)
        => Math.Max(value, MinimumLineOrColumn);

    private static int NormalizeInRange(int? value, int minimum, int maximum)
        => Math.Clamp(value ?? minimum, minimum, maximum);

    private static int? NormalizeNonNegative(int? value)
        => value is null ? null : Math.Max(value.Value, 0);

    private static string NormalizeScope(string? input)
    {
        var normalized = NormalizeString(input).ToLowerInvariant();
        return normalized;
    }

    private static string NormalizeDirection(string? input)
    {
        var normalized = NormalizeString(input).ToLowerInvariant();
        return normalized;
    }

    private static string NormalizeSymbolId(string? input)
        => string.IsNullOrWhiteSpace(input) ? string.Empty : input.Trim();

    private static string? NormalizeOptionalString(string? input)
        => string.IsNullOrWhiteSpace(input) ? null : input.Trim();

    private static string NormalizeString(string? input)
        => string.IsNullOrWhiteSpace(input) ? string.Empty : input.Trim();

}
