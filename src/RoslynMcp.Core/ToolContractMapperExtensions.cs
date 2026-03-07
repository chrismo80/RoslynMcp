using RoslynMcp.Core.Models;

namespace RoslynMcp.Core;

public static class ToolContractMapperExtensions
{
    private const int DefaultMaxDerived = 200;
    private const int MinimumLineOrColumn = 1;

    public static LoadSolutionRequest ToLoadSolutionRequest(this string? solutionHintPath)
        => new(NormalizeOptionalString(solutionHintPath));

    public static UnderstandCodebaseRequest ToUnderstandCodebaseRequest(this string? solutionHintPath)
        => new(NormalizeOptionalString(solutionHintPath));

    public static ExplainSymbolRequest ToExplainSymbolRequest(this string? solutionHintPath, string? path, int? line, int? column)
        => new(
            NormalizeOptionalString(solutionHintPath),
            NormalizeOptionalString(path),
            line.HasValue ? NormalizePosition(line.Value) : null,
            column.HasValue ? NormalizePosition(column.Value) : null);

    public static ListTypesRequest ToListTypesRequest(this string? solutionHintPath, string? projectName,
        string? projectId,
        string? namespacePrefix,
        string? kind,
        string? accessibility,
        int? limit,
        int? offset)
        => new(
            NormalizeOptionalString(solutionHintPath),
            NormalizeOptionalString(projectName),
            NormalizeOptionalString(projectId),
            NormalizeOptionalString(namespacePrefix),
            NormalizeOptionalString(kind)?.ToLowerInvariant(),
            NormalizeOptionalString(accessibility)?.ToLowerInvariant(),
            NormalizeNonNegative(limit),
            NormalizeNonNegative(offset));

    public static ListMembersRequest ToListMembersRequest(this string? solutionHintPath, string? path,
        int? line,
        int? column,
        string? kind,
        string? accessibility,
        string? binding,
        bool? includeInherited,
        int? limit,
        int? offset)
        => new(
            NormalizeOptionalString(solutionHintPath),
            NormalizeOptionalString(path),
            line.HasValue ? NormalizePosition(line.Value) : null,
            column.HasValue ? NormalizePosition(column.Value) : null,
            NormalizeOptionalString(kind)?.ToLowerInvariant(),
            NormalizeOptionalString(accessibility)?.ToLowerInvariant(),
            NormalizeOptionalString(binding)?.ToLowerInvariant(),
            includeInherited ?? false,
            NormalizeNonNegative(limit),
            NormalizeNonNegative(offset));

    public static ResolveSymbolRequest ToResolveSymbolRequest(this string? solutionHintPath, string? path,
        int? line,
        int? column,
        string? qualifiedName,
        string? projectPath,
        string? projectName,
        string? projectId)
        => new(
            NormalizeOptionalString(solutionHintPath),
            NormalizeOptionalString(path),
            line.HasValue ? NormalizePosition(line.Value) : null,
            column.HasValue ? NormalizePosition(column.Value) : null,
            NormalizeOptionalString(qualifiedName),
            NormalizeOptionalString(projectPath),
            NormalizeOptionalString(projectName),
            NormalizeOptionalString(projectId));

    public static TraceFlowRequest ToTraceFlowRequest(this string? solutionHintPath, string? path, int? line, int? column, string? direction, int? depth)
        => new(
            NormalizeOptionalString(solutionHintPath),
            NormalizeOptionalString(path),
            line.HasValue ? NormalizePosition(line.Value) : null,
            column.HasValue ? NormalizePosition(column.Value) : null,
            NormalizeOptionalString(direction)?.ToLowerInvariant(),
            NormalizeNonNegative(depth));

    public static FindCodeSmellsRequest ToFindCodeSmellsRequest(this string? solutionHintPath, int? maxFindings, IReadOnlyList<string>? riskLevels, IReadOnlyList<string>? categories)
        => new(
            NormalizeString(solutionHintPath),
            maxFindings,
            NormalizeOptionalStrings(riskLevels),
            NormalizeOptionalStrings(categories));

    public static ListDependenciesRequest ToListDependenciesRequest(this string? solutionHintPath, string? projectName,
        string? projectId,
        string? direction)
        => new(
            NormalizeOptionalString(solutionHintPath),
            NormalizeOptionalString(projectName),
            NormalizeOptionalString(projectId),
            NormalizeOptionalString(direction)?.ToLowerInvariant());

    public static FindReferencesScopedRequest ToFindReferencesScopedRequest(this string? solutionHintPath, string? scope, string? path)
        => new(NormalizeSymbolId(solutionHintPath), NormalizeScope(scope), NormalizeOptionalString(path));

    public static FindImplementationsRequest ToFindImplementationsRequest(this string? solutionHintPath)
        => new(NormalizeSymbolId(solutionHintPath));

    public static GetTypeHierarchyRequest ToGetTypeHierarchyRequest(this string? solutionHintPath, bool? includeTransitive, int? maxDerived)
        => new(NormalizeSymbolId(solutionHintPath), includeTransitive ?? true, NormalizeNonNegative(maxDerived) ?? DefaultMaxDerived);

    public static RenameSymbolRequest ToRenameSymbolRequest(this string? solutionHintPath, string? newName)
        => new(NormalizeOptionalString(solutionHintPath),
            NormalizeOptionalString(newName));

    public static OrganizeUsingsRequest ToOrganizeUsingsRequest(this string? solutionHintPath, bool removeUnused, bool sortUsings)
        => new(NormalizeOptionalString(solutionHintPath) ?? string.Empty, removeUnused, sortUsings);

    private static int NormalizePosition(int value)
        => Math.Max(value, MinimumLineOrColumn);

    private static int? NormalizeNonNegative(int? value)
        => value is null ? null : Math.Max(value.Value, 0);

    private static string NormalizeScope(string? input)
        => NormalizeString(input).ToLowerInvariant();

    private static string NormalizeSymbolId(string? input)
        => string.IsNullOrWhiteSpace(input) ? string.Empty : input.Trim();

    private static string? NormalizeOptionalString(string? input)
        => string.IsNullOrWhiteSpace(input) ? null : input.Trim();

    private static IReadOnlyList<string>? NormalizeOptionalStrings(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        var normalized = values
            .Select(NormalizeOptionalString)
            .Where(static value => value is not null)
            .Cast<string>()
            .ToArray();

        return normalized.Length == 0 ? Array.Empty<string>() : normalized;
    }

    private static string NormalizeString(string? input)
        => string.IsNullOrWhiteSpace(input) ? string.Empty : input.Trim();
}
