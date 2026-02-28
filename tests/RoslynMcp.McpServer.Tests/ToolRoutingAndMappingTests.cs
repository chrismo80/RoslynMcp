using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Analysis;
using RoslynMcp.Core.Models.Navigation;
using Is.Assertions;

namespace RoslynMcp.McpServer.Tests;

public sealed class ToolRoutingAndMappingTests
{
    private sealed class RecordingNavigationService : INavigationService
    {
        public FindSymbolRequest? LastFindRequest { get; private set; }
        public GetSymbolAtPositionRequest? LastGetSymbolAtPositionRequest { get; private set; }
        public SearchSymbolsRequest? LastSearchRequest { get; private set; }
        public SearchSymbolsScopedRequest? LastScopedSearchRequest { get; private set; }
        public GetSignatureRequest? LastSignatureRequest { get; private set; }
        public FindReferencesRequest? LastReferencesRequest { get; private set; }
        public FindReferencesScopedRequest? LastReferencesScopedRequest { get; private set; }
        public FindImplementationsRequest? LastImplementationsRequest { get; private set; }
        public GetTypeHierarchyRequest? LastTypeHierarchyRequest { get; private set; }
        public GetSymbolOutlineRequest? LastSymbolOutlineRequest { get; private set; }
        public GetCallersRequest? LastCallersRequest { get; private set; }
        public GetCalleesRequest? LastCalleesRequest { get; private set; }
        public GetCallGraphRequest? LastCallGraphRequest { get; private set; }

        public Task<FindSymbolResult> FindSymbolAsync(FindSymbolRequest request, CancellationToken ct)
        {
            LastFindRequest = request;
            return Task.FromResult(new FindSymbolResult(null));
        }

        public Task<SearchSymbolsResult> SearchSymbolsAsync(SearchSymbolsRequest request, CancellationToken ct)
        {
            LastSearchRequest = request;
            return Task.FromResult(new SearchSymbolsResult([], 0));
        }

        public Task<GetSymbolAtPositionResult> GetSymbolAtPositionAsync(GetSymbolAtPositionRequest request, CancellationToken ct)
        {
            LastGetSymbolAtPositionRequest = request;
            return Task.FromResult(new GetSymbolAtPositionResult(null));
        }

        public Task<SearchSymbolsScopedResult> SearchSymbolsScopedAsync(SearchSymbolsScopedRequest request, CancellationToken ct)
        {
            LastScopedSearchRequest = request;
            return Task.FromResult(new SearchSymbolsScopedResult([], 0));
        }

        public Task<GetSignatureResult> GetSignatureAsync(GetSignatureRequest request, CancellationToken ct)
        {
            LastSignatureRequest = request;
            return Task.FromResult(new GetSignatureResult(null, string.Empty));
        }

        public Task<FindReferencesResult> FindReferencesAsync(FindReferencesRequest request, CancellationToken ct)
        {
            LastReferencesRequest = request;
            return Task.FromResult(new FindReferencesResult(null, []));
        }

        public Task<FindReferencesScopedResult> FindReferencesScopedAsync(FindReferencesScopedRequest request, CancellationToken ct)
        {
            LastReferencesScopedRequest = request;
            return Task.FromResult(new FindReferencesScopedResult(null, [], 0));
        }

        public Task<FindImplementationsResult> FindImplementationsAsync(FindImplementationsRequest request, CancellationToken ct)
        {
            LastImplementationsRequest = request;
            return Task.FromResult(new FindImplementationsResult(null, []));
        }

        public Task<GetTypeHierarchyResult> GetTypeHierarchyAsync(GetTypeHierarchyRequest request, CancellationToken ct)
        {
            LastTypeHierarchyRequest = request;
            return Task.FromResult(new GetTypeHierarchyResult(null, [], [], []));
        }

        public Task<GetSymbolOutlineResult> GetSymbolOutlineAsync(GetSymbolOutlineRequest request, CancellationToken ct)
        {
            LastSymbolOutlineRequest = request;
            return Task.FromResult(new GetSymbolOutlineResult(null, [], []));
        }

        public Task<GetCallersResult> GetCallersAsync(GetCallersRequest request, CancellationToken ct)
        {
            LastCallersRequest = request;
            return Task.FromResult(new GetCallersResult(null, []));
        }

        public Task<GetCalleesResult> GetCalleesAsync(GetCalleesRequest request, CancellationToken ct)
        {
            LastCalleesRequest = request;
            return Task.FromResult(new GetCalleesResult(null, []));
        }

        public Task<GetCallGraphResult> GetCallGraphAsync(GetCallGraphRequest request, CancellationToken ct)
        {
            LastCallGraphRequest = request;
            return Task.FromResult(new GetCallGraphResult(null, [], 0, 0));
        }
    }

    private sealed class RecordingAnalysisService : IAnalysisService
    {
        public AnalyzeScopeRequest? LastAnalyzeScopeRequest { get; private set; }

        public Task<AnalyzeSolutionResult> AnalyzeSolutionAsync(AnalyzeSolutionRequest request, CancellationToken ct)
            => Task.FromResult(new AnalyzeSolutionResult([]));

        public Task<AnalyzeScopeResult> AnalyzeScopeAsync(AnalyzeScopeRequest request, CancellationToken ct)
        {
            LastAnalyzeScopeRequest = request;
            return Task.FromResult(new AnalyzeScopeResult(request.Scope, request.Path, [], []));
        }

        public Task<GetCodeMetricsResult> GetCodeMetricsAsync(GetCodeMetricsRequest request, CancellationToken ct)
            => Task.FromResult(new GetCodeMetricsResult([]));
    }
}