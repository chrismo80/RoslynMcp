using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests;

[Collection(FeatureTestsCollection.Name)]
public abstract class ToolTests<TTool>(FeatureTestsFixture fixture, ITestOutputHelper output) where TTool : notnull
{
    protected FeatureTestsFixture Fixture { get; } = fixture;
    
    protected TTool Sut { get; } = fixture.GetRequiredService<TTool>();
    
    protected string TestSolutionDirectory => Path.GetDirectoryName(Fixture.SolutionPath)!;
    
    protected string CodeSmellsPath => GetFilePath("ProjectImpl", "CodeSmells");
    protected string AppOrchestratorPath => GetFilePath("ProjectApp", "AppOrchestrator");
    protected string HierarchyPath => GetFilePath("ProjectCore", "Hierarchy");
    protected string ContractsPath => GetFilePath("ProjectCore", "Contracts");
    
    protected void Trace(string message) => output.WriteLine(typeof(TTool) + ": " + message);
    
    protected string GetFilePath(string project, string file) => Path.Combine(TestSolutionDirectory, project, $"{file}.cs");
}
