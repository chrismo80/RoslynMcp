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
    [McpServerTool(Name = "load_project", Title = "Load Project", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to list types declared in a specific project. It is useful for project-scoped discovery, for finding type symbols before follow-up calls such as load_types or load_member.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("Exact path to a project file (.csproj), obtained from load_solution.")]
        string? projectPath = null
        )
    {
        if (solutionManager.Solution?.Projects.FirstOrDefault(p => Matches(p, projectPath)) is not { } project)
            return new Result([], new ErrorInfo("no project found"));

        if(await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not { } compilation)
            return new Result([], new ErrorInfo("no compilation found"));

        var types = compilation!.GlobalNamespace.GetTypes()
            .Select(ToEntry)
            .Where(entry => entry.Type?.Location.IsHandwritten() ?? false)
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