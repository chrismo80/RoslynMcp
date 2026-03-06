using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests;

[Collection(FeatureTestsCollection.Name)]
public abstract class ToolTests<TTool>(FeatureTestsFixture fixture, ITestOutputHelper output) where TTool : notnull
{
    protected FeatureTestsFixture Fixture { get; } = fixture;
    
    protected TTool Sut { get; } = fixture.GetRequiredService<TTool>();
    
    protected string TestSolutionDirectory => Path.GetDirectoryName(Fixture.SolutionPath)!;
    
    protected void Trace(string message) => output.WriteLine(typeof(TTool) + ": " + message);
}
