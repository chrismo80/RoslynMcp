using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Infrastructure;
using RoslynMcp.Infrastructure.Workspace;
using Is.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Infrastructure.Tests;

[CollectionDefinition("RoslynFindUsages", DisableParallelization = true)]
public sealed class RoslynFindUsagesCollectionDefinition
{
}

[Collection("RoslynFindUsages")]
public sealed class FindUsagesToolsTests
{
    [Fact]
    public async Task FindUsages_WithInvalidSymbolId_ReturnsNotFoundError()
    {
        var service = CreateService(CreateMultiProjectSolution());
        
        // Try to find usages of non-existent symbol
        var result = await service.FindReferencesAsync(
            new FindReferencesRequest("invalid-symbol-id"), 
            CancellationToken.None);
        
        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.SymbolNotFound);
    }

    [Fact]
    public async Task FindUsagesScoped_WithInvalidSymbolId_ReturnsNotFoundError()
    {
        var service = CreateService(CreateMultiProjectSolution());
        
        var result = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(
                "invalid-symbol-id", 
                ReferenceScopes.Solution),
            CancellationToken.None);
        
        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.SymbolNotFound);
    }

    private static Solution CreateMultiProjectSolution()
    {
        var workspace = new AdhocWorkspace();
        
        // Project A - the library
        var projectA = workspace.AddProject("ProjectA", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            });

        var projectACode = """
namespace ProjectA;

public static class Helper
{
    public static void DoWork()
    {
        Console.WriteLine("Working");
    }
}
""";

        var docA = projectA.AddDocument("Helper.cs", SourceText.From(projectACode), filePath: "Helper.cs");
        
        // Project B - references ProjectA
        var projectB = workspace.AddProject("ProjectB", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            })
            .AddProjectReference(new ProjectReference(docA.Project.Id));

        var projectBCode = """
namespace ProjectB;

public class Service
{
    public void Run()
    {
        ProjectA.Helper.DoWork();
    }
}
""";

        var docB = projectB.AddDocument("Service.cs", SourceText.From(projectBCode), filePath: "Service.cs");
        return docB.Project.Solution;
    }

    private static INavigationService CreateService(Solution solution)
    {
        var services = new ServiceCollection();
        services.AddRoslynMcpInfrastructure();
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
