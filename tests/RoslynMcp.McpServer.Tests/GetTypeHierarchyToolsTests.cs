using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Infrastructure;
using RoslynMcp.Infrastructure.Workspace;
using RoslynMcp.McpServer.Tools;
using Is.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace RoslynMcp.McpServer.Tests;

[CollectionDefinition("RoslynGetTypeHierarchy", DisableParallelization = true)]
public sealed class RoslynGetTypeHierarchyCollectionDefinition
{
}

[Collection("RoslynGetTypeHierarchy")]
public sealed class GetTypeHierarchyToolsTests
{
    [Fact]
    public async Task GetTypeHierarchy_ForBaseClass_ReturnsDerivedTypes()
    {
        var solution = CreateTypeHierarchySolution();
        var tool = CreateTool(solution);
        
        // Find BaseClass symbol via NavigationService first
        var navService = CreateNavigationService(solution);
        var search = await navService.SearchSymbolsAsync(
            new SearchSymbolsRequest("BaseClass"), 
            CancellationToken.None);
        
        search.Error.IsNull();
        search.Symbols.Count.IsGreaterThan(0);
        var baseClass = search.Symbols.First();
        
        // Now call the tool
        var result = await tool.GetTypeHierarchyAsync(
            CancellationToken.None,
            baseClass.SymbolId,
            includeTransitive: true,
            maxDerived: 10);
        
        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.Symbol.Name.Is("BaseClass");
        
        // Should find IWorker as implemented interface
        result.ImplementedInterfaces.Any(i => i.Name == "IWorker").IsTrue();
        
        // Should find DerivedClass and LeafClass in derived types
        (result.DerivedTypes.Count >= 2).IsTrue();
        result.DerivedTypes.Any(t => t.Name == "DerivedClass").IsTrue();
        result.DerivedTypes.Any(t => t.Name == "LeafClass").IsTrue();
    }

    [Fact]
    public async Task GetTypeHierarchy_ForInterface_ReturnsImplementations()
    {
        var solution = CreateTypeHierarchySolution();
        var tool = CreateTool(solution);
        var navService = CreateNavigationService(solution);
        
        var search = await navService.SearchSymbolsAsync(
            new SearchSymbolsRequest("IWorker"), 
            CancellationToken.None);
        
        search.Error.IsNull();
        search.Symbols.Count.IsGreaterThan(0);
        var workerInterface = search.Symbols.First();
        
        var result = await tool.GetTypeHierarchyAsync(
            CancellationToken.None,
            workerInterface.SymbolId,
            includeTransitive: true,
            maxDerived: 10);
        
        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.Symbol.Name.Is("IWorker");
        
        // Should find BaseClass as implementation (and transitively DerivedClass, LeafClass)
        result.DerivedTypes.Count.IsGreaterThan(0);
        result.DerivedTypes.Any(t => t.Name == "BaseClass").IsTrue();
    }

    [Fact]
    public async Task GetTypeHierarchy_WithInvalidSymbol_ReturnsError()
    {
        var solution = CreateTypeHierarchySolution();
        var tool = CreateTool(solution);
        
        var result = await tool.GetTypeHierarchyAsync(
            CancellationToken.None,
            "invalid-symbol-id");
        
        result.Error.IsNotNull();
    }

    private static Solution CreateTypeHierarchySolution()
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("HierarchyProject", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            });

        var code = """
namespace Hierarchy;

public interface IWorker
{
    void Work();
}

public class BaseClass : IWorker
{
    public virtual void Work() { }
}

public class DerivedClass : BaseClass
{
    public override void Work() { }
}

public class LeafClass : DerivedClass
{
}
""";

        var document = project.AddDocument("Hierarchy.cs", SourceText.From(code), filePath: "Hierarchy.cs");
        return document.Project.Solution;
    }

    private static GetTypeHierarchyTools CreateTool(Solution solution)
    {
        var services = new ServiceCollection();
        services.AddRoslynMcpMcpServer();
        services.AddSingleton<IRoslynSolutionAccessor>(new TestSolutionAccessor(solution));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<GetTypeHierarchyTools>();
    }

    private static INavigationService CreateNavigationService(Solution solution)
    {
        var services = new ServiceCollection();
        services.AddRoslynMcpMcpServer();
        services.AddSingleton<IRoslynSolutionAccessor>(new TestSolutionAccessor(solution));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<INavigationService>();
    }

    private sealed class TestSolutionAccessor : IRoslynSolutionAccessor
    {
        private readonly Solution _solution;

        public TestSolutionAccessor(Solution solution)
        {
            _solution = solution;
        }

        public Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct)
            => Task.FromResult(((Solution?)_solution, (ErrorInfo?)null));

        public Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct)
            => Task.FromResult(((bool)true, (ErrorInfo?)null));

        public Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct)
            => Task.FromResult((1, (ErrorInfo?)null));
    }
}
