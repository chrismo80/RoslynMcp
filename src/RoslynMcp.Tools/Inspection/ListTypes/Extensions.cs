using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Infrastructure;

namespace RoslynMcp.Tools.Inspection.ListTypes;

internal static partial class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddListTypesTool() => services
			.AddSingleton<Service>()
			.AddSingleton<Tool>();
	}

	extension(string? projectPath)
	{
		public Request ToRequest(
			string? projectName,
			string? projectId,
			string? namespacePrefix,
			string? kind,
			string? accessibility,
			bool? includeSummary,
			bool? includeMembers,
			int? limit,
			int? offset)
			=> new(
				projectPath.NormalizeOptional(),
				projectName.NormalizeOptional(),
				projectId.NormalizeOptional(),
				namespacePrefix.NormalizeOptional(),
				kind.NormalizeOptional(),
				accessibility.NormalizeOptional(),
				includeSummary ?? true,
				includeMembers ?? false,
				limit,
				offset);
	}

	public static bool TryNormalizeTypeKind(this string? kind, out string? normalized)
	{
		normalized = kind.NormalizeOptional()?.ToLowerInvariant();
		if (normalized is null)
			return true;

		if (normalized is "class" or "record" or "interface" or "enum" or "struct")
			return true;

		normalized = null;
		return false;
	}

	public static bool TryNormalizeAccessibility(this string? accessibility, out string? normalized)
	{
		normalized = accessibility.NormalizeOptional()?.Replace('-', '_').ToLowerInvariant();
		if (normalized is null)
			return true;

		if (normalized is "public" or "internal" or "protected" or "private" or "protected_internal" or "private_protected")
			return true;

		normalized = null;
		return false;
	}

	public static (int Offset, int Limit) NormalizePaging(this int? offset, int? limit)
	{
		var normalizedOffset = Math.Max(offset ?? 0, 0);
		var normalizedLimit = limit.HasValue ? Math.Clamp(limit.Value, 0, 500) : 100;
		return (normalizedOffset, normalizedLimit);
	}

	public static string NormalizeAccessibility(this Accessibility accessibility)
		=> accessibility switch
		{
			Accessibility.Public => "public",
			Accessibility.Internal => "internal",
			Accessibility.Protected => "protected",
			Accessibility.Private => "private",
			Accessibility.ProtectedAndInternal => "private_protected",
			Accessibility.ProtectedOrInternal => "protected_internal",
			_ => "not_applicable"
		};

	public static string NormalizeNamespace(this INamespaceSymbol? ns)
		=> ns?.IsGlobalNamespace != false ? string.Empty : ns.ToDisplayString();

	public static string? ToTypeKind(this INamedTypeSymbol symbol)
	{
		if (symbol.IsRecord)
			return "record";

		return symbol.TypeKind switch
		{
			TypeKind.Class => "class",
			TypeKind.Interface => "interface",
			TypeKind.Enum => "enum",
			TypeKind.Struct => "struct",
			_ => null
		};
	}

	public static (string FilePath, int? Line, int? Column) GetDeclarationPosition(this ISymbol symbol)
	{
		var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
		if (location is null)
			return (string.Empty, null, null);

		var span = location.GetLineSpan();
		var start = span.StartLinePosition;
		return (span.Path ?? string.Empty, start.Line + 1, start.Character + 1);
	}

	public static string ToStableId(this ISymbol symbol)
		=> $"{symbol.Kind}:{symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}";

	public static Entry Enrich(this Discovery discovery, bool includeSummary, bool includeMembers)
	{
		var summary = includeSummary ? discovery.Symbol.GetDocumentation()?.Summary : discovery.Entry.Summary;
		var members = includeMembers ? discovery.Symbol.GetDeclaredLightweightMembers() : discovery.Entry.Members;

		return discovery.Entry with { Summary = summary, Members = members };
	}

	public static IReadOnlyList<string> GetDeclaredLightweightMembers(this INamedTypeSymbol type)
		=> type.GetMembers()
			.Where(member => member.DeclaredAccessibility > Accessibility.Private)
			.Select(member => new
			{
				Kind = member.ToMemberKind(),
				Entry = member.ToLightweightMemberEntry(),
				DisplayName = member.Kind == SymbolKind.Method && member is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor ? ctor.ContainingType.Name : member.Name,
				FilePath = member.GetDeclarationPosition().FilePath,
				Signature = member.ToLightweightMemberSignature(),
				Member = member
			})
			.Where(static item => item.Kind is not null)
			.Where(static item => SourceVisibility.ShouldIncludeInHumanResults(item.FilePath))
			.OrderBy(static item => item.Kind, StringComparer.Ordinal)
			.ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
			.ThenBy(static item => item.Signature, StringComparer.Ordinal)
			.ThenBy(static item => item.Member.ToStableId(), StringComparer.Ordinal)
			.Select(static item => item.Entry!)
			.ToArray();

	public static string? ToMemberKind(this ISymbol member)
		=> member switch
		{
			IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "ctor",
			IMethodSymbol method when method.MethodKind == MethodKind.Ordinary || method.MethodKind == MethodKind.UserDefinedOperator || method.MethodKind == MethodKind.Conversion || method.MethodKind == MethodKind.ReducedExtension || method.MethodKind == MethodKind.DelegateInvoke => "method",
			IPropertySymbol => "property",
			IFieldSymbol field when !field.IsImplicitlyDeclared => "field",
			IEventSymbol => "event",
			_ => null
		};

	public static string? ToLightweightMemberEntry(this ISymbol member)
		=> member.ToMemberKind() is null ? null : $"{member.ToStableId()}: {member.DeclaredAccessibility.NormalizeAccessibility()} {member.ToLightweightMemberSignature()}";

	public static string ToLightweightMemberSignature(this ISymbol member)
		=> member switch
		{
			IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } ctor => $"{ctor.ContainingType.Name}({string.Join(", ", ctor.Parameters.Select(FormatParameter))})",
			IMethodSymbol method => $"{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {method.Name}({string.Join(", ", method.Parameters.Select(FormatParameter))})",
			IPropertySymbol property => $"{property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {property.Name} {{ {(property.GetMethod is null ? string.Empty : "get; ")}{(property.SetMethod is null ? string.Empty : property.SetMethod.IsInitOnly ? "init;" : "set;")} }}".Trim(),
			IFieldSymbol field => $"{field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {field.Name}",
			IEventSymbol @event => $"event {@event.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {@event.Name}",
			_ => member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
		};

	private static string FormatParameter(IParameterSymbol parameter)
	{
		var modifier = parameter switch
		{
			{ IsParams: true } => "params ",
			{ RefKind: RefKind.Ref } => "ref ",
			{ RefKind: RefKind.Out } => "out ",
			{ RefKind: RefKind.In } => "in ",
			_ => string.Empty
		};

		return $"{modifier}{parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {parameter.Name}";
	}

	public static IEnumerable<INamedTypeSymbol> EnumerateTypes(this INamespaceSymbol root)
	{
		var stack = new Stack<INamespaceOrTypeSymbol>();
		foreach (var member in root.GetMembers().OrderBy(static member => member.Name, StringComparer.Ordinal))
			stack.Push(member);

		while (stack.Count > 0)
		{
			var current = stack.Pop();
			switch (current)
			{
				case INamedTypeSymbol namedType:
					yield return namedType;
					foreach (var nested in namedType.GetTypeMembers().OrderByDescending(static member => member.Name, StringComparer.Ordinal))
						stack.Push(nested);
					break;
				case INamespaceSymbol ns:
					foreach (var member in ns.GetMembers().OrderByDescending(static member => member.Name, StringComparer.Ordinal))
						stack.Push(member);
					break;
			}
		}
	}

	public static VisibilityAssessment AssessPaths(IEnumerable<string?> paths)
	{
		var handwritten = 0;
		var generated = 0;
		var unknown = 0;

		foreach (var path in paths)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				unknown++;
				continue;
			}

			if (SourceVisibility.IsGeneratedLike(path))
				generated++;
			else
				handwritten++;
		}

		var visibility = handwritten > 0 && generated > 0 ? SourceBiases.Mixed : handwritten > 0 ? SourceBiases.Handwritten : generated > 0 ? SourceBiases.Generated : SourceBiases.Unknown;
		return new VisibilityAssessment(visibility, handwritten, generated, unknown);
	}

	public static string DetermineResultSourceBias(IEnumerable<string?> paths)
	{
		var assessment = AssessPaths(paths);
		return assessment.Visibility;
	}

	extension(Result result)
	{
		public Result WithWorkspaceRelativePaths()
			=> result with
			{
				Types = result.Types.Select(type => type.WithWorkspaceRelativePaths()).ToArray(),
				Error = result.Error.WithWorkspaceRelativePaths()
			};
	}

	extension(Entry entry)
	{
		public Entry WithWorkspaceRelativePaths()
			=> entry with { Location = entry.Location.WithWorkspaceRelativePaths() };
	}

	extension(SourceLocation? location)
	{
		public SourceLocation? WithWorkspaceRelativePaths()
			=> location is null ? null : location with { FilePath = location.FilePath.ToWorkspaceRelativePathIfPossible() };
	}

	extension(ErrorInfo? error)
	{
		public ErrorInfo? WithWorkspaceRelativePaths()
		{
			if (error?.Details is null || error.Details.Count == 0)
				return error;

			Dictionary<string, string>? updated = null;
			foreach (var pair in error.Details)
			{
				if (pair.Key is not ("path" or "filepath" or "projectpath" or "provided"))
					continue;

				var outward = pair.Value.ToWorkspaceRelativePathIfPossible();
				if (string.Equals(outward, pair.Value, StringComparison.Ordinal))
					continue;

				updated ??= new Dictionary<string, string>(error.Details, StringComparer.Ordinal);
				updated[pair.Key] = outward;
			}

			return updated is null ? error : error with { Details = updated };
		}
	}

	public static SymbolDocumentation? GetDocumentation(this ISymbol symbol)
	{
		var xml = symbol.GetDocumentationCommentXml(cancellationToken: CancellationToken.None);
		if (string.IsNullOrWhiteSpace(xml))
			return null;

		XElement root;
		try
		{
			root = XElement.Parse($"<root>{xml}</root>", LoadOptions.PreserveWhitespace);
		}
		catch (XmlException)
		{
			return null;
		}

		var summary = NormalizeElementText(root.Descendants("summary").FirstOrDefault());
		var returns = NormalizeElementText(root.Descendants("returns").FirstOrDefault());
		var parameters = root.Descendants("param")
			.Select(CreateParameterDocumentation)
			.Where(static parameter => parameter is not null)
			.Cast<SymbolParameterDocumentation>()
			.ToArray();

		return summary is null && returns is null && parameters.Length == 0 ? null : new SymbolDocumentation(summary, returns, parameters);
	}

	private static SymbolParameterDocumentation? CreateParameterDocumentation(XElement element)
	{
		var name = NormalizeText(element.Attribute("name")?.Value);
		var description = NormalizeElementText(element);
		return name is null || description is null ? null : new SymbolParameterDocumentation(name, description);
	}

	private static string? NormalizeElementText(XElement? element)
	{
		if (element is null)
			return null;

		var builder = new StringBuilder();
		AppendNodeText(element, builder);
		return NormalizeText(builder.ToString());
	}

	private static void AppendNodeText(XNode node, StringBuilder builder)
	{
		switch (node)
		{
			case XText text:
				builder.Append(text.Value);
				return;
			case XElement element when element.Name.LocalName is "see" or "seealso":
				builder.Append(NormalizeSymbolReference(element.Attribute("cref")?.Value));
				return;
			case XElement element when element.Name.LocalName is "paramref" or "typeparamref":
				builder.Append(element.Attribute("name")?.Value);
				return;
			case XElement element:
				foreach (var child in element.Nodes())
					AppendNodeText(child, builder);
				return;
		}
	}

	private static string? NormalizeSymbolReference(string? cref)
	{
		var normalized = NormalizeText(cref);
		if (normalized is null)
			return null;

		return normalized.Length > 2 && normalized[1] == ':' ? normalized[2..] : normalized;
	}

	private static string? NormalizeText(string? value)
		=> string.IsNullOrWhiteSpace(value) ? null : WhitespacePattern().Replace(value.Trim(), " ");

	[GeneratedRegex("\\s+")]
	private static partial Regex WhitespacePattern();
}
