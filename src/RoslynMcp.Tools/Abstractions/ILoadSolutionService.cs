using RoslynMcp.Tools.Inspection.LoadSolution;

namespace RoslynMcp.Tools.Abstractions;

public interface ILoadSolutionService
{
	Task<Result> LoadSolutionAsync(Request request, CancellationToken ct);
}