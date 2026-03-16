using RoslynMcp.Core;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

/// <summary>
/// Lists members (method, property, field, event, constructor) of a type with optional inheritance traversal.
/// Filters by member kind and accessibility.
/// </summary>
internal sealed class ListMembersHandler(CodeUnderstandingQueryService queries)
{
    public async Task<ListMembersResult> HandleAsync(ListMembersRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before listing members.",
            request.Path,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ListMembersResult([], 0, request.IncludeInherited,
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before listing members."));
        }

        if (!request.Kind.TryNormalizeMemberKind(out var normalizedMemberKind))
        {
            return new ListMembersResult([], 0, request.IncludeInherited,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "kind must be one of: method, property, field, event, ctor.",
                    "Retry list_members with a supported kind filter or omit kind.",
                    ("field", "kind"),
                    ("provided", request.Kind ?? string.Empty),
                    ("expected", "method|property|field|event|ctor")));
        }

        if (!request.Accessibility.TryNormalizeAccessibility(out var normalizedAccessibility))
        {
            return new ListMembersResult([], 0, request.IncludeInherited,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "accessibility must be one of: public, internal, protected, private, protected_internal, private_protected.",
                    "Retry list_members with a supported accessibility filter or omit accessibility.",
                    ("field", "accessibility"),
                    ("provided", request.Accessibility ?? string.Empty)));
        }

        if (!request.Binding.TryNormalizeBinding(out var normalizedBinding))
        {
            return new ListMembersResult([], 0, request.IncludeInherited,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "binding must be one of: static, instance.",
                    "Retry list_members with binding=static or binding=instance, or omit binding.",
                    ("field", "binding"),
                    ("provided", request.Binding ?? string.Empty),
                    ("expected", "static|instance")));
        }

        var typeSymbol = await queries.ResolveTypeSymbolAsync(request, solution, ct).ConfigureAwait(false);
        if (typeSymbol.Error != null)
            return new ListMembersResult([], 0, request.IncludeInherited, typeSymbol.Error);

        var symbols = request.IncludeInherited
            ? typeSymbol.Symbol!.CollectMembersWithInheritance()
            : typeSymbol.Symbol!.GetMembers();

        var entries = symbols
            .Select(member => member.ToMemberEntry(normalizedMemberKind, normalizedAccessibility, normalizedBinding))
            .Where(static entry => entry != null)
            .Select(static entry => entry!)
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Signature, StringComparer.Ordinal)
            .ThenBy(static item => item.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var (offset, limit) = request.Offset.NormalizePaging(request.Limit);
        var paged = entries.Skip(offset).Take(limit).ToArray();
        return new ListMembersResult(paged, entries.Length, request.IncludeInherited);
    }
}
