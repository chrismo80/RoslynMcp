using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Infrastructure.Agent;
using Is.Assertions;

namespace RoslynMcp.Infrastructure.Tests;

public sealed class FlowTraceServiceTests
{
    [Theory]
    [InlineData("downstream")]
    [InlineData("upstream")]
    [InlineData("both")]
    public async Task TraceFlow_PropagatesNavigationErrorsAndReturnsEmptyEdges(string direction)
    {
        var navigation = new RecordingNavigationService
        {
            RootSymbol = new SymbolDescriptor(
                "symbol-id",
                "Call",
                "Method",
                "Sample.Service",
                "Sample",
                new SourceLocation("/repo/Sample.cs", 10, 5))
        };

        var expectedError = new ErrorInfo(
            ErrorCodes.InternalError,
            "Call graph failed.",
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["operation"] = "trace"
            });

        navigation.CallersError = expectedError;
        navigation.CalleesError = expectedError;
        navigation.CallGraphError = expectedError;

        var service = new FlowTraceService(navigation);

        var result = await service.TraceFlowAsync(
            new TraceFlowRequest(SymbolId: "symbol-id", Direction: direction, Depth: 2),
            CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.InternalError);
        result.Error?.Details?.ContainsKey("nextAction").IsTrue();
        result.Edges.Count.Is(0);
        result.Transitions.Count.Is(0);
    }

    private sealed class RecordingNavigationService : INavigationService
    {
        public SymbolDescriptor? RootSymbol { get; set; }
        public ErrorInfo? CallersError { get; set; }
        public ErrorInfo? CalleesError { get; set; }
        public ErrorInfo? CallGraphError { get; set; }

        public Task<FindSymbolResult> FindSymbolAsync(FindSymbolRequest request, CancellationToken ct)
            => Task.FromResult(new FindSymbolResult(RootSymbol));

        public Task<GetSymbolAtPositionResult> GetSymbolAtPositionAsync(GetSymbolAtPositionRequest request, CancellationToken ct)
            => Task.FromResult(new GetSymbolAtPositionResult(RootSymbol));

        public Task<SearchSymbolsResult> SearchSymbolsAsync(SearchSymbolsRequest request, CancellationToken ct)
            => Task.FromResult(new SearchSymbolsResult(Array.Empty<SymbolDescriptor>(), 0));

        public Task<SearchSymbolsScopedResult> SearchSymbolsScopedAsync(SearchSymbolsScopedRequest request, CancellationToken ct)
            => Task.FromResult(new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(), 0));

        public Task<GetSignatureResult> GetSignatureAsync(GetSignatureRequest request, CancellationToken ct)
            => Task.FromResult(new GetSignatureResult(null, string.Empty));

        public Task<FindReferencesResult> FindReferencesAsync(FindReferencesRequest request, CancellationToken ct)
            => Task.FromResult(new FindReferencesResult(null, Array.Empty<SourceLocation>()));

        public Task<FindReferencesScopedResult> FindReferencesScopedAsync(FindReferencesScopedRequest request, CancellationToken ct)
            => Task.FromResult(new FindReferencesScopedResult(null, Array.Empty<SourceLocation>(), 0));

        public Task<FindImplementationsResult> FindImplementationsAsync(FindImplementationsRequest request, CancellationToken ct)
            => Task.FromResult(new FindImplementationsResult(null, Array.Empty<SymbolDescriptor>()));

        public Task<GetTypeHierarchyResult> GetTypeHierarchyAsync(GetTypeHierarchyRequest request, CancellationToken ct)
            => Task.FromResult(new GetTypeHierarchyResult(null, Array.Empty<SymbolDescriptor>(), Array.Empty<SymbolDescriptor>(), Array.Empty<SymbolDescriptor>()));

        public Task<GetSymbolOutlineResult> GetSymbolOutlineAsync(GetSymbolOutlineRequest request, CancellationToken ct)
            => Task.FromResult(new GetSymbolOutlineResult(null, Array.Empty<SymbolMemberOutline>(), Array.Empty<string>()));

        public Task<GetCallersResult> GetCallersAsync(GetCallersRequest request, CancellationToken ct)
            => Task.FromResult(new GetCallersResult(RootSymbol, Array.Empty<CallEdge>(), CallersError));

        public Task<GetCalleesResult> GetCalleesAsync(GetCalleesRequest request, CancellationToken ct)
            => Task.FromResult(new GetCalleesResult(RootSymbol, Array.Empty<CallEdge>(), CalleesError));

        public Task<GetCallGraphResult> GetCallGraphAsync(GetCallGraphRequest request, CancellationToken ct)
            => Task.FromResult(new GetCallGraphResult(RootSymbol, Array.Empty<CallEdge>(), 0, 0, CallGraphError));
    }
}
