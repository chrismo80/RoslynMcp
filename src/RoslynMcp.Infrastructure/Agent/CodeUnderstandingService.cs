using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Agent.Handlers;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Workspace;

namespace RoslynMcp.Infrastructure.Agent;

public sealed class CodeUnderstandingService : ICodeUnderstandingService
{
    private readonly UnderstandCodebaseHandler _understandCodebaseHandler;
    private readonly ListTypesHandler _listTypesHandler;
    private readonly ListMembersHandler _listMembersHandler;
    private readonly ResolveSymbolHandler _resolveSymbolHandler;
    private readonly ExplainSymbolHandler _explainSymbolHandler;
    private readonly ListDependenciesHandler _listDependenciesHandler;

    public CodeUnderstandingService(
        IRoslynSolutionAccessor solutionAccessor,
        ISolutionSessionService solutionSessionService,
        IWorkspaceBootstrapService workspaceBootstrapService,
        IAnalysisService analysisService,
        INavigationService navigationService,
        ISymbolLookupService symbolLookupService)
    {
        var queries = new CodeUnderstandingQueryService(
            solutionAccessor,
            solutionSessionService,
            workspaceBootstrapService,
            symbolLookupService,
            navigationService);

        _understandCodebaseHandler = new UnderstandCodebaseHandler(queries, analysisService);
        _listTypesHandler = new ListTypesHandler(queries);
        _listMembersHandler = new ListMembersHandler(queries);
        _resolveSymbolHandler = new ResolveSymbolHandler(queries, symbolLookupService);
        _explainSymbolHandler = new ExplainSymbolHandler(queries, navigationService, solutionAccessor, symbolLookupService);
        _listDependenciesHandler = new ListDependenciesHandler(queries);
    }

    public Task<UnderstandCodebaseResult> UnderstandCodebaseAsync(UnderstandCodebaseRequest request, CancellationToken ct)
        => _understandCodebaseHandler.HandleAsync(request, ct);

    public Task<ExplainSymbolResult> ExplainSymbolAsync(ExplainSymbolRequest request, CancellationToken ct)
        => _explainSymbolHandler.HandleAsync(request, ct);

    public Task<ListTypesResult> ListTypesAsync(ListTypesRequest request, CancellationToken ct)
        => _listTypesHandler.HandleAsync(request, ct);

    public Task<ListMembersResult> ListMembersAsync(ListMembersRequest request, CancellationToken ct)
        => _listMembersHandler.HandleAsync(request, ct);

    public Task<ResolveSymbolResult> ResolveSymbolAsync(ResolveSymbolRequest request, CancellationToken ct)
        => _resolveSymbolHandler.HandleAsync(request, ct);

    public Task<ListDependenciesResult> ListDependenciesAsync(ListDependenciesRequest request, CancellationToken ct)
        => _listDependenciesHandler.HandleAsync(request, ct);
}
