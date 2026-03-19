using RoslynMcp.Core.Models;

namespace RoslynMcp.Core;

public static class ToolContractMapperExtensions
{
    private const int DefaultMaxDerived = 200;
    private const int MinimumLineOrColumn = 1;

    extension(string? solutionHintPath)
    {
        public LoadSolutionRequest ToLoadSolutionRequest()
            => new(NormalizeOptionalString(solutionHintPath));

        public UnderstandProjectsRequest ToUnderstandProjectsRequest()
            => new(NormalizeOptionalString(solutionHintPath));

        public ExplainSymbolRequest ToExplainSymbolRequest(string? path, int? line, int? column)
            => new(
                NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(path),
                line.HasValue ? NormalizePosition(line.Value) : null,
                column.HasValue ? NormalizePosition(column.Value) : null);

        public ListTypesRequest ToListTypesRequest(string? projectName,
            string? projectId,
            string? namespacePrefix,
            string? kind,
            string? accessibility,
            bool? includeSummary,
            bool? includeMembers,
            int? limit,
            int? offset)
            => new(
                NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(projectName),
                NormalizeOptionalString(projectId),
                NormalizeOptionalString(namespacePrefix),
                NormalizeOptionalString(kind)?.ToLowerInvariant(),
                NormalizeOptionalString(accessibility)?.ToLowerInvariant(),
                includeSummary ?? true,
                includeMembers ?? false,
                NormalizeNonNegative(limit),
                NormalizeNonNegative(offset));

        public ListMembersRequest ToListMembersRequest(string? path,
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

        public ResolveSymbolRequest ToResolveSymbolRequest(string? path,
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

        public ResolveSymbolsBatchRequest ToResolveSymbolsBatchRequest(IReadOnlyList<ResolveSymbolBatchEntry>? entries)
            => new(
                entries?.Select(static entry => new ResolveSymbolBatchEntry(
                        NormalizeOptionalString(entry.SymbolId),
                        NormalizeOptionalString(entry.Path),
                        entry.Line.HasValue ? NormalizePosition(entry.Line.Value) : null,
                        entry.Column.HasValue ? NormalizePosition(entry.Column.Value) : null,
                        NormalizeOptionalString(entry.QualifiedName),
                        NormalizeOptionalString(entry.ProjectPath),
                        NormalizeOptionalString(entry.ProjectName),
                        NormalizeOptionalString(entry.ProjectId),
                        NormalizeOptionalString(entry.Label)))
                    .ToArray()
                ?? Array.Empty<ResolveSymbolBatchEntry>());

        public TraceFlowRequest ToTraceFlowRequest(string? path, int? line, int? column, string? direction, int? depth, bool? includePossibleTargets)
            => new(
                NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(path),
                line.HasValue ? NormalizePosition(line.Value) : null,
                column.HasValue ? NormalizePosition(column.Value) : null,
                NormalizeOptionalString(direction)?.ToLowerInvariant(),
                NormalizeNonNegative(depth),
                includePossibleTargets ?? false);

        public FindCodeSmellsRequest ToFindCodeSmellsRequest(int? maxFindings, IReadOnlyList<string>? riskLevels, IReadOnlyList<string>? categories, string? reviewMode)
            => new(
                NormalizeString(solutionHintPath),
                maxFindings,
                NormalizeOptionalStrings(riskLevels),
                NormalizeOptionalStrings(categories),
                NormalizeOptionalString(reviewMode)?.ToLowerInvariant());

        public RunTestsRequest ToRunTestsRequest(string? filter)
            => new(
                NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(filter));

        public FindReferencesScopedRequest ToFindReferencesScopedRequest(string? scope, string? path)
            => new(NormalizeSymbolId(solutionHintPath), NormalizeScope(scope), NormalizeOptionalString(path));

        public FindImplementationsRequest ToFindImplementationsRequest()
            => new(NormalizeSymbolId(solutionHintPath));

        public GetTypeHierarchyRequest ToGetTypeHierarchyRequest(bool? includeTransitive, int? maxDerived)
            => new(NormalizeSymbolId(solutionHintPath), includeTransitive ?? true, NormalizeNonNegative(maxDerived) ?? DefaultMaxDerived);

        public RenameSymbolRequest ToRenameSymbolRequest(string? newName)
            => new(NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(newName));

        public FormatDocumentRequest ToFormatDocumentRequest()
            => new(NormalizeOptionalString(solutionHintPath) ?? string.Empty);

        public AddMethodRequest ToAddMethodRequest(
            string? name,
            string? returnType,
            string? accessibility,
            IReadOnlyList<string>? modifiers,
            IReadOnlyList<string>? parameters,
            string? body)
            => new(
                NormalizeSymbolId(solutionHintPath),
                new MethodInsertionSpec(
                    NormalizeString(name),
                    NormalizeString(returnType).NormalizeEscapedTypeSyntax(),
                    NormalizeString(accessibility),
                    NormalizeOptionalStrings(modifiers) ?? Array.Empty<string>(),
                    ParseMethodParameters(parameters),
                    NormalizeString(body).NormalizeEscapedNewlines()));

        public DeleteMethodRequest ToDeleteMethodRequest()
            => new(NormalizeSymbolId(solutionHintPath));

        public ReplaceMethodRequest ToReplaceMethodRequest(
            string? name,
            string? returnType,
            string? accessibility,
            IReadOnlyList<string>? modifiers,
            IReadOnlyList<string>? parameters,
            string? body)
            => new(
                NormalizeSymbolId(solutionHintPath),
                new MethodInsertionSpec(
                    NormalizeString(name),
                    NormalizeString(returnType).NormalizeEscapedTypeSyntax(),
                    NormalizeString(accessibility),
                    NormalizeOptionalStrings(modifiers) ?? Array.Empty<string>(),
                    ParseMethodParameters(parameters),
                    NormalizeString(body).NormalizeEscapedNewlines()));

        public ReplaceMethodBodyRequest ToReplaceMethodBodyRequest(string? body)
            => new(NormalizeSymbolId(solutionHintPath), NormalizeString(body).NormalizeEscapedNewlines());
    }

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

    private static IReadOnlyList<MethodParameterSpec> ParseMethodParameters(IReadOnlyList<string>? parameters)
    {
        var normalized = NormalizeOptionalStrings(parameters);
        if (normalized is null || normalized.Count == 0)
        {
            return Array.Empty<MethodParameterSpec>();
        }

        var results = new List<MethodParameterSpec>(normalized.Count);
        foreach (var parameter in normalized)
        {
            var separatorIndex = parameter.LastIndexOf(' ');
            if (separatorIndex <= 0 || separatorIndex == parameter.Length - 1)
            {
                results.Add(new MethodParameterSpec(string.Empty, parameter));
                continue;
            }

            var type = parameter[..separatorIndex].Trim().NormalizeEscapedTypeSyntax();
            var name = parameter[(separatorIndex + 1)..].Trim();
            results.Add(new MethodParameterSpec(name, type));
        }

        return results;
    }
}
