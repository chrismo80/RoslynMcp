using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Contracts;

public interface ITestInspectionService
{
    Task<RunTestsResult> RunTestsAsync(RunTestsRequest request, CancellationToken ct);
}
