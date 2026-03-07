using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections;

[Collection(SharedSandboxFeatureTestsCollection.CollectionName)]
public abstract class SharedToolTests<TTool> where TTool : notnull
{
    private readonly ITestOutputHelper _output;

    protected SharedToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    {
        Context = fixture.Context;
        _output = output;
        Sut = Context.GetRequiredService<TTool>();
    }

    protected SharedSandboxContext Context { get; }

    protected TTool Sut { get; }

    protected string TestSolutionDirectory => Context.TestSolutionDirectory;

    protected string CodeSmellsPath => Context.GetFilePath("ProjectImpl", "CodeSmells");
    protected string AppOrchestratorPath => Context.GetFilePath("ProjectApp", "AppOrchestrator");
    protected string HierarchyPath => Context.GetFilePath("ProjectCore", "Hierarchy");
    protected string ContractsPath => Context.GetFilePath("ProjectCore", "Contracts");

    protected string GetFilePath(string project, string file) => Context.GetFilePath(project, file);

    protected void Trace(string message) => _output.WriteLine(typeof(TTool) + ": " + message);
}