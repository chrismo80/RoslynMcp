using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Documentation;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

/// <summary>
/// Explains a symbol: returns signature, XML docs, outline, and key references.
/// Aggregates navigation data for AI agent context.
/// </summary>
internal sealed class ExplainSymbolHandler(
    CodeUnderstandingQueryService queries,
    INavigationService navigationService,
    IRoslynSolutionAccessor solutionAccessor,
    ISymbolLookupService symbolLookupService,
    ISymbolDocumentationProvider symbolDocumentationProvider)
{
    public async Task<ExplainSymbolResult> HandleAsync(ExplainSymbolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (_, bootstrapError) = await queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before explaining symbols.",
            request.Path,
            ct).ConfigureAwait(false);

        if (bootstrapError != null)
            return new ExplainSymbolResult(null, string.Empty, string.Empty, [], [], null, bootstrapError);

        var symbolResult = await queries.ResolveSymbolAtRequestAsync(request.SymbolId, request.Path, request.Line, request.Column, ct).ConfigureAwait(false);
        if (symbolResult.Symbol == null)
        {
            return new ExplainSymbolResult(null, string.Empty, string.Empty, [], [], null,
                AgentErrorInfo.Normalize(symbolResult.Error, "Call explain_symbol with symbolId or path+line+column for an existing symbol."));
        }

        var signature = await navigationService.GetSignatureAsync(new GetSignatureRequest(symbolResult.Symbol.SymbolId), ct).ConfigureAwait(false);
        var outline = await navigationService.GetSymbolOutlineAsync(new GetSymbolOutlineRequest(symbolResult.Symbol.SymbolId, 1), ct).ConfigureAwait(false);
        var references = await navigationService.FindReferencesAsync(new FindReferencesRequest(symbolResult.Symbol.SymbolId), ct).ConfigureAwait(false);

        var keyReferences = references.References
            .GroupBy(static reference => reference.FilePath, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(group => new ReferenceFileGroup(
                group.Key,
                group.Take(3)
                    .Select(static reference => new ReferencePosition(reference.Line, reference.Column))
                    .ToArray()))
            .ToArray();

        var impactHints = references.References
            .GroupBy(static r => Path.GetFileName(r.FilePath), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(group => new ImpactHint(group.Key, "high reference density", group.Count()))
            .ToArray();

        var symbol = await ResolveSymbolAsync(symbolResult.Symbol.SymbolId, ct).ConfigureAwait(false);
        var roleSummary = BuildRoleSummary(symbolResult.Symbol, symbol, outline, references);
        var documentation = symbol is null ? null : MapDocumentation(symbolDocumentationProvider.GetDocumentation(symbol));

        return new ExplainSymbolResult(
            ToCompactSummary(symbolResult.Symbol),
            roleSummary,
            signature.Signature,
            keyReferences.Length == 0 ? null : keyReferences,
            impactHints,
            documentation,
            AgentErrorInfo.Normalize(signature.Error ?? outline.Error ?? references.Error,
                "Retry explain_symbol for a resolvable symbol in the loaded solution."));
    }

    private async Task<ISymbol?> ResolveSymbolAsync(string symbolId, CancellationToken ct)
    {
        var (solution, error) = await solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution == null || error != null)
        {
            return null;
        }

        return await symbolLookupService.ResolveSymbolAsync(symbolId, solution, ct).ConfigureAwait(false);
    }

    private static string BuildRoleSummary(
        SymbolDescriptor descriptor,
        ISymbol? symbol,
        GetSymbolOutlineResult outline,
        FindReferencesResult references)
    {
        if (symbol == null)
        {
            return outline.Members.Count == 0
                ? $"{descriptor.Kind} '{descriptor.Name}'."
                : $"{descriptor.Kind} '{descriptor.Name}' with {outline.Members.Count} top-level members.";
        }

        return symbol switch
        {
            INamedTypeSymbol namedType => BuildTypeSummary(namedType, outline, references),
            IMethodSymbol method => BuildMethodSummary(method, references),
            IPropertySymbol property => BuildPropertySummary(property, references),
            IFieldSymbol field => BuildFieldSummary(field, references),
            _ => BuildFallbackSummary(symbol, outline, references)
        };
    }

    private static SymbolDocumentationInfo? MapDocumentation(SymbolDocumentation? documentation)
    {
        if (documentation == null)
        {
            return null;
        }

        var parameters = documentation.Parameters.Count == 0
            ? null
            : documentation.Parameters
                .Select(static parameter => new SymbolDocumentationParameter(parameter.Name, parameter.Description))
                .ToArray();

        if (documentation.Summary == null && documentation.Returns == null && parameters == null)
        {
            return null;
        }

        return new SymbolDocumentationInfo(documentation.Summary, documentation.Returns, parameters);
    }

    private static CompactSymbolSummary ToCompactSummary(SymbolDescriptor symbol)
        => new(
            symbol.SymbolId,
            symbol.Name,
            symbol.Kind,
            symbol.DeclarationLocation,
            symbol.ContainingType ?? NormalizeNamespaceValue(symbol.ContainingNamespace));

    private static string BuildTypeSummary(INamedTypeSymbol symbol, GetSymbolOutlineResult outline, FindReferencesResult references)
    {
        var responsibility = symbol.TypeKind switch
        {
            TypeKind.Interface => "defines the contract",
            TypeKind.Class when symbol.IsAbstract => "provides a reusable base abstraction",
            TypeKind.Class => "owns the runtime behavior",
            TypeKind.Struct => "packages value-oriented state",
            TypeKind.Enum => "declares the allowed value set",
            _ => "represents the symbol's primary abstraction"
        };

        var memberShape = DescribeMemberShape(outline.Members);
        var collaborators = string.Join(", ", CollectTypeCollaborators(symbol).Take(3));
        var impact = references.References.Count == 0
            ? "It currently has no discovered incoming references."
            : $"Edits likely affect {references.References.Count} referencing location{(references.References.Count == 1 ? string.Empty : "s")}.";

        return string.IsNullOrWhiteSpace(collaborators)
            ? $"{symbol.Name} is a {symbol.TypeKind.ToString().ToLowerInvariant()} in {NormalizeNamespace(symbol.ContainingNamespace)} that {responsibility}. {memberShape}. {impact}"
            : $"{symbol.Name} is a {symbol.TypeKind.ToString().ToLowerInvariant()} in {NormalizeNamespace(symbol.ContainingNamespace)} that {responsibility}. {memberShape}. Key collaborators: {collaborators}. {impact}";
    }

    private static string BuildMethodSummary(IMethodSymbol symbol, FindReferencesResult references)
    {
        var parameters = symbol.Parameters.Length == 0
            ? "no parameters"
            : string.Join(", ", symbol.Parameters.Select(static parameter => parameter.Type.Name));
        var impact = references.References.Count == 0
            ? "No discovered call sites reference it yet."
            : $"It is referenced from {references.References.Count} location{(references.References.Count == 1 ? string.Empty : "s")}.";

        return $"{symbol.Name} is a method on {symbol.ContainingType?.Name ?? "its containing type"} that returns {symbol.ReturnType.Name} and works with {parameters}. {impact}";
    }

    private static string BuildPropertySummary(IPropertySymbol symbol, FindReferencesResult references)
    {
        var access = symbol.SetMethod == null ? "read-only" : "read/write";
        return $"{symbol.Name} is a {access} property on {symbol.ContainingType?.Name ?? "its containing type"} exposing {symbol.Type.Name}. It is referenced from {references.References.Count} location{(references.References.Count == 1 ? string.Empty : "s")}.";
    }

    private static string BuildFieldSummary(IFieldSymbol symbol, FindReferencesResult references)
    {
        var storage = symbol.IsConst ? "constant value" : symbol.IsReadOnly ? "read-only state" : "mutable state";
        return $"{symbol.Name} is {storage} on {symbol.ContainingType?.Name ?? "its containing type"} with type {symbol.Type.Name}. It is referenced from {references.References.Count} location{(references.References.Count == 1 ? string.Empty : "s")}.";
    }

    private static string BuildFallbackSummary(ISymbol symbol, GetSymbolOutlineResult outline, FindReferencesResult references)
        => outline.Members.Count == 0
            ? $"{symbol.Kind} '{symbol.Name}' is referenced from {references.References.Count} location{(references.References.Count == 1 ? string.Empty : "s")}."
            : $"{symbol.Kind} '{symbol.Name}' exposes {outline.Members.Count} top-level members and is referenced from {references.References.Count} location{(references.References.Count == 1 ? string.Empty : "s")}.";

    private static string DescribeMemberShape(IReadOnlyList<SymbolMemberOutline> members)
    {
        if (members.Count == 0)
        {
            return "It has no top-level members";
        }

        var methods = members.Count(static member => string.Equals(member.Kind, "Method", StringComparison.Ordinal));
        var properties = members.Count(static member => string.Equals(member.Kind, "Property", StringComparison.Ordinal));
        var fields = members.Count(static member => string.Equals(member.Kind, "Field", StringComparison.Ordinal));
        return $"It exposes {methods} methods, {properties} properties, and {fields} fields";
    }

    private static IEnumerable<string> CollectTypeCollaborators(INamedTypeSymbol symbol)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in symbol.InstanceConstructors.SelectMany(static ctor => ctor.Parameters))
        {
            if (seen.Add(parameter.Type.Name) && !string.IsNullOrWhiteSpace(parameter.Type.Name))
            {
                yield return parameter.Type.Name;
            }
        }

        foreach (var member in symbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (seen.Add(member.Type.Name) && !string.IsNullOrWhiteSpace(member.Type.Name))
                yield return member.Type.Name;
        }

        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (seen.Add(member.Type.Name) && !string.IsNullOrWhiteSpace(member.Type.Name))
                yield return member.Type.Name;
        }
    }

    private static string NormalizeNamespace(INamespaceSymbol? symbol)
        => symbol?.IsGlobalNamespace != false ? "the global namespace" : symbol.ToDisplayString();

    private static string? NormalizeNamespaceValue(string? value)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, "<global namespace>", StringComparison.Ordinal)
            ? null
            : value;
}
