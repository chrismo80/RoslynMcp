using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Infrastructure.Services;

namespace RoslynMcp.Tools.Inspection.ExplainSymbol;

public sealed class Service(Infrastructure.Services.Workspace workspace, SymbolLookup symbolLookup)
{
	public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
		if (session is null)
		{
			return new Result(null, string.Empty, string.Empty, [], [], null, new ErrorInfo(
				"no_solution_loaded",
				"No solution is currently loaded.",
				new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["nextAction"] = "Call load_solution first to select a solution before explaining symbols."
				}));
		}

		var symbol = await ResolveSymbolAsync(request, session.Solution, cancellationToken).ConfigureAwait(false);
		if (symbol is null)
		{
			return new Result(null, string.Empty, string.Empty, [], [], null, new ErrorInfo(
				"invalid_input",
				"Call explain_symbol with symbolId or path+line+column for an existing symbol.",
				new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["nextAction"] = "Call explain_symbol with symbolId or path+line+column for an existing symbol."
				}));
		}

		var references = await symbol.FindReferencesAsync(session.Solution, cancellationToken).ConfigureAwait(false);
		var keyReferences = references
			.GroupBy(static reference => reference.FilePath, StringComparer.Ordinal)
			.OrderByDescending(static group => group.Count())
			.ThenBy(static group => group.Key, StringComparer.Ordinal)
			.Take(3)
			.Select(group => new ReferenceFileGroup(group.Key, group.Take(3).Select(static reference => new ReferencePosition(reference.Line, reference.Column)).ToArray()))
			.ToArray();

		var impactHints = references
			.GroupBy(static reference => Path.GetFileName(reference.FilePath), StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(static group => group.Count())
			.ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Take(3)
			.Select(group => new ImpactHint(group.Key, "high reference density", group.Count()))
			.ToArray();

		return new Result(
			symbol.ToCompactSymbol(),
			BuildRoleSummary(symbol, references.Count),
			symbol.BuildSignature(),
			keyReferences.Length == 0 ? null : keyReferences,
			impactHints,
			symbol.GetDocumentation().ToDocumentationInfo()).WithWorkspaceRelativePaths();
	}

	private async Task<ISymbol?> ResolveSymbolAsync(Request request, Solution solution, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(request.SymbolId))
			return await symbolLookup.ResolveSymbolAsync(request.SymbolId!, solution, cancellationToken).ConfigureAwait(false);

		if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
			return await symbolLookup.GetSymbolAtPositionAsync(solution, request.Path!, request.Line.Value, request.Column.Value, cancellationToken).ConfigureAwait(false);

		return null;
	}

	private static string BuildRoleSummary(ISymbol symbol, int referenceCount)
		=> symbol switch
		{
			INamedTypeSymbol namedType => BuildTypeSummary(namedType, referenceCount),
			IMethodSymbol method => BuildMethodSummary(method, referenceCount),
			IPropertySymbol property => BuildPropertySummary(property, referenceCount),
			IFieldSymbol field => BuildFieldSummary(field, referenceCount),
			_ => $"{symbol.Kind} '{symbol.Name}' is referenced from {referenceCount} location{(referenceCount == 1 ? string.Empty : "s")}."
		};

	private static string BuildTypeSummary(INamedTypeSymbol symbol, int referenceCount)
	{
		var responsibility = symbol.TypeKind switch
		{
			TypeKind.Interface => "defines the contract",
			TypeKind.Class when symbol.IsAbstract => "provides a reusable base abstraction",
			TypeKind.Class => "owns the runtime behavior",
			TypeKind.Struct => "packages value-oriented state",
			TypeKind.Enum => "declares the allowed value set",
			_ => "represents the primary abstraction"
		};

		var methods = symbol.GetMembers().OfType<IMethodSymbol>().Count(static member => member.MethodKind == MethodKind.Ordinary);
		var properties = symbol.GetMembers().OfType<IPropertySymbol>().Count();
		var fields = symbol.GetMembers().OfType<IFieldSymbol>().Count(static member => !member.IsImplicitlyDeclared);
		return $"{symbol.Name} is a {symbol.TypeKind.ToString().ToLowerInvariant()} in {NormalizeNamespace(symbol.ContainingNamespace)} that {responsibility}. It exposes {methods} methods, {properties} properties, and {fields} fields. Edits likely affect {referenceCount} referencing location{(referenceCount == 1 ? string.Empty : "s")}.";
	}

	private static string BuildMethodSummary(IMethodSymbol symbol, int referenceCount)
	{
		var parameters = symbol.Parameters.Length == 0 ? "no parameters" : string.Join(", ", symbol.Parameters.Select(static parameter => parameter.Type.Name));
		return $"{symbol.Name} is a method on {symbol.ContainingType?.Name ?? "its containing type"} that returns {symbol.ReturnType.Name} and works with {parameters}. It is referenced from {referenceCount} location{(referenceCount == 1 ? string.Empty : "s")}.";
	}

	private static string BuildPropertySummary(IPropertySymbol symbol, int referenceCount)
	{
		var access = symbol.SetMethod is null ? "read-only" : "read/write";
		return $"{symbol.Name} is a {access} property on {symbol.ContainingType?.Name ?? "its containing type"} exposing {symbol.Type.Name}. It is referenced from {referenceCount} location{(referenceCount == 1 ? string.Empty : "s")}.";
	}

	private static string BuildFieldSummary(IFieldSymbol symbol, int referenceCount)
	{
		var storage = symbol.IsConst ? "constant value" : symbol.IsReadOnly ? "read-only state" : "mutable state";
		return $"{symbol.Name} is {storage} on {symbol.ContainingType?.Name ?? "its containing type"} with type {symbol.Type.Name}. It is referenced from {referenceCount} location{(referenceCount == 1 ? string.Empty : "s")}.";
	}

	private static string NormalizeNamespace(INamespaceSymbol? symbol)
		=> symbol?.IsGlobalNamespace != false ? "the global namespace" : symbol.ToDisplayString();
}
