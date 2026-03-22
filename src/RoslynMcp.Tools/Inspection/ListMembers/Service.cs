using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Infrastructure.Services;

namespace RoslynMcp.Tools.Inspection.ListMembers;

public sealed class Service(Infrastructure.Services.Workspace workspace, SymbolLookup symbolLookup)
{
    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return new Result([], 0, request.IncludeInherited, new ErrorInfo(
                "no_solution_loaded",
                "No solution is currently loaded.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nextAction"] = "Call load_solution first to select a solution before listing members."
                }));
        }

        if (!request.Kind.TryNormalizeMemberKind(out var normalizedKind))
            return Invalid(request, "kind", request.Kind ?? string.Empty, "kind must be one of: method, property, field, event, or ctor.", "method|property|field|event|ctor");

        if (!request.Accessibility.TryNormalizeAccessibility(out var normalizedAccessibility))
            return Invalid(request, "accessibility", request.Accessibility ?? string.Empty, "accessibility must be one of: public, internal, protected, private, protected_internal, or private_protected.");

        if (!request.Binding.TryNormalizeBinding(out var normalizedBinding))
            return Invalid(request, "binding", request.Binding ?? string.Empty, "binding must be one of: static or instance.", "static|instance");

        var (Symbol, Error) = await ResolveTypeAsync(request, session.Solution, symbolLookup, cancellationToken).ConfigureAwait(false);
        if (Error is not null)
            return new Result([], 0, request.IncludeInherited, Error).WithWorkspaceRelativePaths();

        var symbols = request.IncludeInherited
            ? Symbol!.CollectMembersWithInheritance()
            : Symbol!.GetMembers();

        var entries = symbols
            .Select(member => member.ToEntry(normalizedKind, normalizedAccessibility, normalizedBinding))
            .Where(static entry => entry is not null)
            .Select(static entry => entry!)
            .OrderBy(static entry => entry.Kind, StringComparer.Ordinal)
            .ThenBy(static entry => entry.DisplayName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Signature, StringComparer.Ordinal)
            .ThenBy(static entry => entry.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var (offset, limit) = request.Offset.NormalizePaging(request.Limit);
        var paged = entries.Skip(offset).Take(limit).ToArray();

        return new Result(paged, entries.Length, request.IncludeInherited).WithWorkspaceRelativePaths();
    }

    private static async Task<(INamedTypeSymbol? Symbol, ErrorInfo? Error)> ResolveTypeAsync(Request request, Solution solution, SymbolLookup symbolLookup, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.TypeSymbolId))
        {
            var symbol = await SymbolLookup.ResolveSymbolAsync(request.TypeSymbolId!, solution, cancellationToken).ConfigureAwait(false);
            if (symbol is null)
            {
                return (null, new ErrorInfo(
                    "invalid_input",
                    $"typeSymbolId '{request.TypeSymbolId}' could not be resolved.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["field"] = "typeSymbolId",
                        ["provided"] = request.TypeSymbolId,
                        ["expected"] = "type symbolId returned by list_types",
                        ["nextAction"] = "Call list_types first to select a valid typeSymbolId."
                    }));
            }

            if (symbol is not INamedTypeSymbol namedType)
            {
                return (null, new ErrorInfo(
                    "invalid_input",
                    "typeSymbolId must resolve to a type symbol.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["field"] = "typeSymbolId",
                        ["provided"] = request.TypeSymbolId,
                        ["expected"] = "type symbolId",
                        ["nextAction"] = "Call list_types and pass a type symbolId, not a member symbolId."
                    }));
            }

            return (namedType, null);
        }

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
        {
            var symbol = await SymbolLookup.GetSymbolAtPositionAsync(solution, request.Path!, request.Line.Value, request.Column.Value, cancellationToken).ConfigureAwait(false);
            if (symbol is null)
            {
                return (null, new ErrorInfo(
                    "symbol_not_found",
                    "No symbol found at the provided source position.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["path"] = request.Path,
                        ["nextAction"] = "Call list_members with a valid typeSymbolId from list_types, or provide a valid source position."
                    }));
            }

            var typeSymbol = symbol as INamedTypeSymbol ?? symbol.ContainingType;
            if (typeSymbol is not null)
                return (typeSymbol, null);

            return (null, new ErrorInfo(
                "invalid_input",
                "Resolved symbol is not a type and has no containing type.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["field"] = "typeSymbolId",
                    ["nextAction"] = "Call list_members with a symbolId that resolves to a type declaration."
                }));
        }

        return (null, new ErrorInfo(
            "invalid_input",
            "Provide typeSymbolId or path/line/column.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nextAction"] = "Call list_members with a typeSymbolId from list_types, or provide a source position."
            }));
    }

    private static Result Invalid(Request request, string field, string provided, string message, string? expected = null)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["field"] = field,
            ["provided"] = provided
        };

        if (!string.IsNullOrWhiteSpace(expected))
            details["expected"] = expected;

        return new Result([], 0, request.IncludeInherited, new ErrorInfo("invalid_input", message, details));
    }
}
