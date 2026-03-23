using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test;

//[Collection("Inspection")]
public abstract class InspectionTests(ITestOutputHelper output)
{
	protected ITestOutputHelper Output => output;
}