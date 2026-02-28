using RoslynMcp.Core.Models.Agent;

namespace RoslynMcp.Core.Contracts;

public interface IWorkspaceBootstrapService
{
    Task<LoadSolutionResult> LoadSolutionAsync(LoadSolutionRequest request, CancellationToken ct);
}

public interface ICodeUnderstandingService
{
    Task<UnderstandCodebaseResult> UnderstandCodebaseAsync(UnderstandCodebaseRequest request, CancellationToken ct);
    Task<ExplainSymbolResult> ExplainSymbolAsync(ExplainSymbolRequest request, CancellationToken ct);
    Task<ListTypesResult> ListTypesAsync(ListTypesRequest request, CancellationToken ct);
    Task<ListMembersResult> ListMembersAsync(ListMembersRequest request, CancellationToken ct);
    Task<ResolveSymbolResult> ResolveSymbolAsync(ResolveSymbolRequest request, CancellationToken ct);
    Task<ListDependenciesResult> ListDependenciesAsync(ListDependenciesRequest request, CancellationToken ct);
}

public interface IFlowTraceService
{
    Task<TraceFlowResult> TraceFlowAsync(TraceFlowRequest request, CancellationToken ct);
}

public interface ICodeSmellFindingService
{
    Task<FindCodeSmellsResult> FindCodeSmellsAsync(FindCodeSmellsRequest request, CancellationToken ct);
}
