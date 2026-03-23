using System.Diagnostics;
using Is.Assertions;
using RoslynMcp.Tools.Inspection.LoadSolution;
using RoslynMcp.Tools.Inspection.UnderstandProjects;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Test;

public class Tests
{
    [Test]
    public async Task Test1()
    {
        var ct = new CancellationTokenSource();
        
        var workspaceManager = new WorkspaceManager();
        var solutionManager = new SolutionManager();
        var symbolManager = new SymbolManager();

        workspaceManager.SetWorkspaceDirectory(@"D:\code\Private\Github\RoslynMcp\tests\TestSolution");
        
        var loadSolutionTool = new LoadSolutionTool(workspaceManager, solutionManager);

        var result1 = await loadSolutionTool.Execute(ct.Token);

        result1.IsNotNull();
        
        var understandProjectsTool = new UnderstandProjectsTool(solutionManager, symbolManager);
        
        var result2 = await understandProjectsTool.Execute(ct.Token, "deep");
        
        result2.IsNotNull();
        
        foreach(var project in result2.Projects)
        foreach(var type in project.Types)
            Console.WriteLine(project.Name + " - " + type);
    }
}