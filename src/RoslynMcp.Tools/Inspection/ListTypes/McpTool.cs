using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.ListTypes;

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager
    ) : Tool
{
    [McpServerTool(Name = "list_types", Title = "List Types", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to list types declared in a specific loaded project. It is useful for project-scoped discovery, for finding type symbols before follow-up calls such as list_members or resolve_symbol, and for optionally enriching only the returned type entries with XML summaries or lightweight declared-member previews. For automation, prefer projectPath as the stable selector; projectId is snapshot-local to the active workspace snapshot. Results prefer handwritten declarations by default and report source bias, completeness, and degraded discovery hints.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("Exact path to a project file (.csproj). Specify only one of projectPath, projectName, or projectId.")]
        string? projectPath = null,
        [Description(
            "When true, includes a lightweight preview of declared members for each returned type entry. This is not full member metadata: each member is returned as a single normalized accessibility-plus-signature string, and only members declared on that type are included. Enrichment is applied only to the returned type entries. Use list_members as the detailed follow-up tool. When omitted or false, members are omitted.")]
        bool? includeMembers = null)
    {
        if (solutionManager.Solution?.Projects.FirstOrDefault(p => Matches(p, projectPath)) is not { } project)
            return new Result([], new ErrorInfo("no project found"));

        if(await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not { } compilation)
            return new Result([], new ErrorInfo("no compilation found"));

        var types = compilation.Assembly.GlobalNamespace.EnumerateTypes().Select(ToEntry);

        return new Result(types.ToArray());
    }

    private bool Matches(Project project, string input)
    {
        return project.Name == input || project.FilePath == workspaceManager.ToAbsolutePath(input);
    }

    private Entry ToEntry(INamedTypeSymbol symbol)
    {
        return new Entry(
            symbol.Name,
            symbolManager.ToId(symbol),
            GetDeclarationPosition(symbol),
            symbol.ToTypeKind(),
            symbol.Arity,
            null,
            GetDeclaredLightweightMembers(symbol)
            );
    }

    private Location GetDeclarationPosition(INamedTypeSymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);

        if (location is null)
            return new Location(string.Empty, 0, 0);

        var span = location.GetLineSpan();
        var start = span.StartLinePosition;

        return new Location(workspaceManager.ToRelativePathIfPossible(span.Path), start.Line + 1, start.Character + 1);
    }

    private IReadOnlyList<string> GetDeclaredLightweightMembers(INamedTypeSymbol type)
    {
        return type.GetMembers()
            .Where(member => member.DeclaredAccessibility > Accessibility.Private)
            .Select(member => new
            {
                Symbol = symbolManager.ToId(member),
                Kind = member.ToMemberKind(),
                DisplayName = $"{member.DeclaredAccessibility.ToText()} {member.ToLightweightMemberSignature()}"
            })
            .Where(static item => item.Kind != null)
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .Select(static item => $"{item.Symbol}: {item.DisplayName}")
            .ToArray();
    }
}