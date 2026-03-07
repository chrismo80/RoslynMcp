using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using RoslynMcp.Infrastructure.Navigation;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

internal sealed class ListTypesHandler
{
    private readonly CodeUnderstandingQueryService _queries;

    public ListTypesHandler(CodeUnderstandingQueryService queries)
    {
        _queries = queries;
    }

    public async Task<ListTypesResult> HandleAsync(ListTypesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await _queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before listing types.",
            request.ProjectPath,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ListTypesResult(
                Array.Empty<TypeListEntry>(),
                0,
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before listing types."));
        }

        if (!request.Kind.TryNormalizeTypeKind(out var normalizedKind))
        {
            return new ListTypesResult(
                Array.Empty<TypeListEntry>(),
                0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "kind must be one of: class, record, interface, enum, struct.",
                    "Retry list_types with a supported kind filter or omit kind.",
                    ("field", "kind"),
                    ("provided", request.Kind ?? string.Empty),
                    ("expected", "class|record|interface|enum|struct")));
        }

        if (!request.Accessibility.TryNormalizeAccessibility(out var normalizedAccessibility))
        {
            return new ListTypesResult(
                Array.Empty<TypeListEntry>(),
                0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "accessibility must be one of: public, internal, protected, private, protected_internal, private_protected.",
                    "Retry list_types with a supported accessibility filter or omit accessibility.",
                    ("field", "accessibility"),
                    ("provided", request.Accessibility ?? string.Empty)));
        }

        var selectedProjects = solution.ResolveProjectSelector(
            request.ProjectPath,
            request.ProjectName,
            request.ProjectId,
            selectorRequired: true,
            toolName: "list_types",
            out var selectorError);

        if (selectorError != null)
        {
            return new ListTypesResult(Array.Empty<TypeListEntry>(), 0, selectorError);
        }

        var namespacePrefix = request.NamespacePrefix.NormalizeOptional();
        var entries = new List<TypeListEntry>();

        foreach (var project in selectedProjects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                continue;
            }

            foreach (var type in compilation.Assembly.GlobalNamespace.EnumerateTypes())
            {
                if (!type.Locations.Any(static location => location.IsInSource))
                {
                    continue;
                }

                var kind = type.ToTypeKind();
                if (kind == null)
                {
                    continue;
                }

                if (normalizedKind != null && !string.Equals(kind, normalizedKind, StringComparison.Ordinal))
                {
                    continue;
                }

                var accessibility = type.DeclaredAccessibility.NormalizeAccessibility();
                if (normalizedAccessibility != null && !string.Equals(accessibility, normalizedAccessibility, StringComparison.Ordinal))
                {
                    continue;
                }

                var typeNamespace = type.ContainingNamespace.NormalizeNamespace();
                if (namespacePrefix != null && !typeNamespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var (filePath, line, column) = type.GetDeclarationPosition();
                if (!SourceVisibility.ShouldIncludeInHumanResults(filePath))
                {
                    continue;
                }

                entries.Add(new TypeListEntry(
                    type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    SymbolIdentity.CreateId(type),
                    filePath,
                    line,
                    column,
                    kind,
                    type.IsPartial(),
                    type.Arity > 0 ? type.Arity : null));
            }
        }

        var ordered = entries
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Arity ?? 0)
            .ThenBy(static item => item.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var (offset, limit) = request.Offset.NormalizePaging(request.Limit);
        var paged = ordered.Skip(offset).Take(limit).ToArray();
        return new ListTypesResult(paged, ordered.Length);
    }
}
