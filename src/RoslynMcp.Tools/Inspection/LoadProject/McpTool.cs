using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadProject;

public sealed record Result(
    IReadOnlyList<Entry> Types,
    ErrorInfo? Error = null);

public sealed record Entry(
    TypeSymbol? Type = null,
    IReadOnlyList<string>? Members = null);

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
        string? projectPath = null
        )
    {
        if (solutionManager.Solution?.Projects.FirstOrDefault(p => Matches(p, projectPath)) is not { } project)
            return new Result([], new ErrorInfo("no project found"));

        if(await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not { } compilation)
            return new Result([], new ErrorInfo("no compilation found"));

        var types = compilation!.GlobalNamespace.GetTypes()
            .Select(ToEntry)
            .OrderByDescending(e => e.Members?.Count)
            .ToList();

        return new Result(types);
    }

    private bool Matches(Project project, string input)
    {
        return project.Name == input || project.FilePath == workspaceManager.ToAbsolutePath(input);
    }

    private Entry ToEntry(INamedTypeSymbol symbol)
    {
        return new Entry(TypeSymbol.From(symbol, symbolManager, workspaceManager), symbol.MembersPreview(symbolManager, workspaceManager));
    }
}