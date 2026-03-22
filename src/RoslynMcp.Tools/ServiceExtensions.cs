using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Inspection.ExplainSymbol;
using RoslynMcp.Tools.Inspection.FindCallees;
using RoslynMcp.Tools.Inspection.FindCallers;
using RoslynMcp.Tools.Inspection.FindImplementations;
using RoslynMcp.Tools.Inspection.FindUsages;
using RoslynMcp.Tools.Inspection.GetTypeHierarchy;
using RoslynMcp.Tools.Inspection.ListMembers;
using RoslynMcp.Tools.Inspection.ListTypes;
using RoslynMcp.Tools.Inspection.LoadSolution;
using RoslynMcp.Tools.Inspection.ResolveSymbol;
using RoslynMcp.Tools.Inspection.ResolveSymbols;
using RoslynMcp.Tools.Inspection.RunTests;
using RoslynMcp.Tools.Inspection.TraceCallFlow;
using RoslynMcp.Tools.Inspection.UnderstandProjects;

namespace RoslynMcp.Tools;

public static class ServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection WithRoslynMcp() => services
            .AddInfrastructure()
            .AddTools();

        private IServiceCollection AddInfrastructure() => services
            .AddSingleton<Workspace>()
            .AddSingleton<SymbolLookup>();

        private IServiceCollection AddTools() => services
            .AddLoadSolutionTool()
            .AddUnderstandProjectsTool()
            .AddExplainSymbolTool()
            .AddFindCallersTool()
            .AddFindCalleesTool()
            .AddFindImplementationsTool()
            .AddFindUsagesTool()
            .AddGetTypeHierarchyTool()
            .AddListMembersTool()
            .AddResolveSymbolTool()
            .AddResolveSymbolsTool()
            .AddRunTestsTool()
            .AddTraceCallFlowTool()
            .AddListTypesTool();
    }
}
