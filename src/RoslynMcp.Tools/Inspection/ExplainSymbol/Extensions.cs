using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Inspection.ExplainSymbol;

internal static class Extensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddExplainSymbolTool() => services
			.AddSingleton<Service>()
			.AddSingleton<Tool>();
	}

	internal static CompactSymbol ToCompactSymbol(this ISymbol symbol)
	{
		var (filePath, line, column) = symbol.GetDeclarationPosition();
		return new CompactSymbol(
			symbol.ToStableId(),
			symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
			symbol.Kind.ToString(),
			CreateOptionalSourceLocation(filePath, line, column),
			symbol.ContainingType?.Name ?? symbol.ContainingNamespace.NormalizeOwner());
	}

	internal static async Task<IReadOnlyList<SourceLocation>> FindReferencesAsync(this ISymbol symbol, Solution solution, CancellationToken cancellationToken)
	{
		var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
		var unique = new HashSet<string>(StringComparer.Ordinal);
		var locations = new List<SourceLocation>();

		foreach (var reference in references)
		{
			cancellationToken.ThrowIfCancellationRequested();

			foreach (var location in reference.Locations)
			{
				if (!location.Location.IsInSource)
					continue;

				var source = location.Location.ToSourceLocation();
				var key = $"{source.FilePath}:{source.Line}:{source.Column}";
				if (unique.Add(key))
					locations.Add(source);
			}
		}

		return locations
			.OrderBy(static location => location.FilePath, StringComparer.Ordinal)
			.ThenBy(static location => location.Line)
			.ThenBy(static location => location.Column)
			.ToArray();
	}

	internal static SourceLocation ToSourceLocation(this Location location)
	{
		var span = location.GetLineSpan();
		var start = span.StartLinePosition;
		return new SourceLocation(span.Path ?? string.Empty, start.Line + 1, start.Character + 1);
	}

	internal static SymbolDocumentation? GetDocumentation(this ISymbol symbol)
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

	internal static SymbolDocumentationInfo? ToDocumentationInfo(this SymbolDocumentation? documentation)
	{
		if (documentation is null)
			return null;

		var parameters = documentation.Parameters.Count == 0
			? null
			: documentation.Parameters.Select(static parameter => new SymbolDocumentationParameter(parameter.Name, parameter.Description)).ToArray();

		return documentation.Summary is null && documentation.Returns is null && parameters is null
			? null
			: new SymbolDocumentationInfo(documentation.Summary, documentation.Returns, parameters);
	}

	internal static string BuildSignature(this ISymbol symbol)
		=> symbol switch
		{
			IMethodSymbol method => method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
			IPropertySymbol property => property.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
			IFieldSymbol field => field.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
			INamedTypeSymbol type => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
			_ => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
		};

	internal static Result WithWorkspaceRelativePaths(this Result result)
		=> result with
		{
			Symbol = result.Symbol.WithWorkspaceRelativePaths(),
			KeyReferences = result.KeyReferences?.Select(static group => group.WithWorkspaceRelativePaths()).ToArray(),
			Error = result.Error.WithWorkspaceRelativePaths()
		};

	private static CompactSymbol? WithWorkspaceRelativePaths(this CompactSymbol? symbol)
		=> symbol is null ? null : symbol with { Location = symbol.Location.WithWorkspaceRelativePaths() };

	private static ReferenceFileGroup WithWorkspaceRelativePaths(this ReferenceFileGroup group)
		=> group with { FilePath = group.FilePath.ToWorkspaceRelativePathIfPossible() };

	private static SourceLocation? WithWorkspaceRelativePaths(this SourceLocation? location)
		=> location is null ? null : location with { FilePath = location.FilePath.ToWorkspaceRelativePathIfPossible() };

	private static ErrorInfo? WithWorkspaceRelativePaths(this ErrorInfo? error)
	{
		if (error?.Details is null || error.Details.Count == 0)
			return error;

		Dictionary<string, string>? updated = null;
		foreach (var pair in error.Details)
		{
			if (pair.Key is not ("path" or "filepath" or "provided"))
				continue;

			var outward = pair.Value.ToWorkspaceRelativePathIfPossible();
			if (string.Equals(outward, pair.Value, StringComparison.Ordinal))
				continue;

			updated ??= new Dictionary<string, string>(error.Details, StringComparer.Ordinal);
			updated[pair.Key] = outward;
		}

		return updated is null ? error : error with { Details = updated };
	}

	private static string? NormalizeOwner(this INamespaceSymbol? symbol)
		=> symbol?.IsGlobalNamespace != false ? null : symbol.ToDisplayString();

	private static SourceLocation? CreateOptionalSourceLocation(string filePath, int? line, int? column)
		=> string.IsNullOrWhiteSpace(filePath) || !line.HasValue || !column.HasValue ? null : new SourceLocation(filePath, line.Value, column.Value);

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
				break;
			case XElement element:
				foreach (var child in element.Nodes())
					AppendNodeText(child, builder);
				break;
		}
	}

	private static string? NormalizeText(string? text)
		=> string.IsNullOrWhiteSpace(text) ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
