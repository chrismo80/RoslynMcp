using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.McpServer.Tools;
using Is.Assertions;

namespace RoslynMcp.McpServer.Tests;

public sealed class AgentIntentToolRoutingTests
{
    [Fact]
    public async Task IntentTools_AreRoutableAndNormalizeInputs()
    {
        var bootstrap = new RecordingWorkspaceBootstrapService();
        var understanding = new RecordingCodeUnderstandingService();
        var flow = new RecordingFlowTraceService();
        var discovery = new RecordingCodeSmellFindingService();

        var workspace = new LoadSolutionTools(bootstrap);
        var understand = new UnderstandCodebaseTools(understanding);
        var explain = new ExplainSymbolTools(understanding);
        var listTypes = new ListTypesTools(understanding);
        var listMembers = new ListMembersTools(understanding);
        var resolve = new ResolveSymbolTools(understanding);
        var trace = new TraceFlowTools(flow);
        var modification = new CodeSmellTools(discovery);

        await workspace.LoadSolutionAsync(CancellationToken.None, "  ./sample.sln  ");
        await understand.UnderstandCodebaseAsync(CancellationToken.None, "  deep  ");
        await explain.ExplainSymbolAsync(CancellationToken.None, "  sym  ", "  a.cs  ", -1, 0);
        await listTypes.ListTypesAsync(CancellationToken.None, "  Sample.csproj  ", "  SampleProject ", " abc ", "  Sample  ", "  CLASS ", "  PUBLIC  ", -4, -7);
        await listMembers.ListMembersAsync(CancellationToken.None, "  type-id ", "  a.cs  ", -1, 0, " METHOD ", " PUBLIC ", " STATIC ", null, -2, -3);
        await resolve.ResolveSymbolAsync(CancellationToken.None, "  sym  ", "  a.cs  ", -1, 0, "  Sample.Service.Call  ", "  Sample.csproj  ", " SampleProject ", " abc ");
        await trace.TraceFlowAsync(CancellationToken.None, "  sym  ", "  a.cs  ", -1, 0, "  BOTH  ", -9);
        await modification.FindCodeSmellsAsync("  app.cs  ", CancellationToken.None);

        bootstrap.LastRequest?.SolutionHintPath.Is("./sample.sln");
        understanding.LastUnderstandRequest?.Profile.Is("deep");
        understanding.LastExplainRequest?.SymbolId.Is("sym");
        understanding.LastExplainRequest?.Line.Is(1);
        understanding.LastExplainRequest?.Column.Is(1);
        understanding.LastListTypesRequest?.ProjectPath.Is("Sample.csproj");
        understanding.LastListTypesRequest?.ProjectName.Is("SampleProject");
        understanding.LastListTypesRequest?.ProjectId.Is("abc");
        understanding.LastListTypesRequest?.Kind.Is("class");
        understanding.LastListTypesRequest?.Limit.Is(0);
        understanding.LastListTypesRequest?.Offset.Is(0);
        understanding.LastListMembersRequest?.TypeSymbolId.Is("type-id");
        understanding.LastListMembersRequest?.Kind.Is("method");
        understanding.LastListMembersRequest?.Accessibility.Is("public");
        understanding.LastListMembersRequest?.Binding.Is("static");
        understanding.LastListMembersRequest?.Line.Is(1);
        understanding.LastListMembersRequest?.Column.Is(1);
        understanding.LastListMembersRequest?.Limit.Is(0);
        understanding.LastListMembersRequest?.Offset.Is(0);
        understanding.LastResolveSymbolRequest?.SymbolId.Is("sym");
        understanding.LastResolveSymbolRequest?.QualifiedName.Is("Sample.Service.Call");
        flow.LastRequest?.Direction.Is("both");
        flow.LastRequest?.Depth.Is(0);
        discovery.LastRequest?.Path.Is("app.cs");
    }

    private sealed class RecordingWorkspaceBootstrapService : IWorkspaceBootstrapService
    {
        public LoadSolutionRequest? LastRequest { get; private set; }

        public Task<LoadSolutionResult> LoadSolutionAsync(LoadSolutionRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new LoadSolutionResult(null, string.Empty, string.Empty, Array.Empty<ProjectSummary>(), new DiagnosticsSummary(0, 0, 0, 0)));
        }
    }

    private sealed class RecordingCodeUnderstandingService : ICodeUnderstandingService
    {
        public UnderstandCodebaseRequest? LastUnderstandRequest { get; private set; }
        public ExplainSymbolRequest? LastExplainRequest { get; private set; }
        public ListTypesRequest? LastListTypesRequest { get; private set; }
        public ListMembersRequest? LastListMembersRequest { get; private set; }
        public ResolveSymbolRequest? LastResolveSymbolRequest { get; private set; }

        public Task<UnderstandCodebaseResult> UnderstandCodebaseAsync(UnderstandCodebaseRequest request, CancellationToken ct)
        {
            LastUnderstandRequest = request;
            return Task.FromResult(new UnderstandCodebaseResult("standard", Array.Empty<ModuleSummary>(), Array.Empty<HotspotSummary>()));
        }

        public Task<ExplainSymbolResult> ExplainSymbolAsync(ExplainSymbolRequest request, CancellationToken ct)
        {
            LastExplainRequest = request;
            return Task.FromResult(new ExplainSymbolResult(null, string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<ImpactHint>()));
        }

        public Task<ListTypesResult> ListTypesAsync(ListTypesRequest request, CancellationToken ct)
        {
            LastListTypesRequest = request;
            return Task.FromResult(new ListTypesResult(Array.Empty<TypeListEntry>(), 0));
        }

        public Task<ListMembersResult> ListMembersAsync(ListMembersRequest request, CancellationToken ct)
        {
            LastListMembersRequest = request;
            return Task.FromResult(new ListMembersResult(Array.Empty<MemberListEntry>(), 0, request.IncludeInherited));
        }

        public Task<ResolveSymbolResult> ResolveSymbolAsync(ResolveSymbolRequest request, CancellationToken ct)
        {
            LastResolveSymbolRequest = request;
            return Task.FromResult(new ResolveSymbolResult(null, false, Array.Empty<ResolveSymbolCandidate>()));
        }

        public Task<ListDependenciesResult> ListDependenciesAsync(ListDependenciesRequest request, CancellationToken ct)
        {
            return Task.FromResult(new ListDependenciesResult(Array.Empty<ProjectDependency>(), 0));
        }
    }

    private sealed class RecordingFlowTraceService : IFlowTraceService
    {
        public TraceFlowRequest? LastRequest { get; private set; }

        public Task<TraceFlowResult> TraceFlowAsync(TraceFlowRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new TraceFlowResult(null, "both", 1, Array.Empty<RoslynMcp.Core.Models.Navigation.CallEdge>(), Array.Empty<FlowTransition>()));
        }
    }

    private sealed class RecordingCodeSmellFindingService : ICodeSmellFindingService
    {
        public FindCodeSmellsRequest? LastRequest { get; private set; }

        public Task<FindCodeSmellsResult> FindCodeSmellsAsync(FindCodeSmellsRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new FindCodeSmellsResult(Array.Empty<CodeSmellMatch>(), Array.Empty<string>()));
        }
    }
}
