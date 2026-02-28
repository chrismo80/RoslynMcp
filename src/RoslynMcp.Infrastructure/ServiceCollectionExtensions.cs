using RoslynMcp.Core.Contracts;
using RoslynMcp.Infrastructure.Agent;
using RoslynMcp.Infrastructure.Analysis;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Refactoring;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRoslynMcpInfrastructure(this IServiceCollection services) => services
		.AddSingleton<ISessionStateStore, SessionStateStore>()
		.AddSingleton<IWorkspaceRootDiscovery, WorkspaceRootDiscovery>()
		.AddSingleton<ISolutionPathResolver, SolutionPathResolver>()
		.AddSingleton<IMSBuildRegistrationGate, MsBuildRegistrationGate>()
		.AddSingleton<ISessionWorkspaceLoader, SessionWorkspaceLoader>()
		.AddSingleton<RoslynSolutionSessionService>(provider =>
			new RoslynSolutionSessionService(
				provider.GetRequiredService<ISessionStateStore>(),
				provider.GetRequiredService<IWorkspaceRootDiscovery>(),
				provider.GetRequiredService<ISolutionPathResolver>(),
				provider.GetRequiredService<ISessionWorkspaceLoader>(),
				provider.GetService<Microsoft.Extensions.Logging.ILogger<RoslynSolutionSessionService>>()))
		.AddSingleton<ISolutionSessionService>(p => p.GetRequiredService<RoslynSolutionSessionService>())
		.AddSingleton<IRoslynSolutionAccessor>(p => p.GetRequiredService<RoslynSolutionSessionService>())
		.AddSingleton<IRoslynAnalyzerCatalog, RoslynatorAnalyzerCatalog>()
		.AddSingleton<IAnalysisDiagnosticsRunner, AnalysisDiagnosticsRunner>()
		.AddSingleton<IRoslynSymbolIdFactory, RoslynSymbolIdFactory>()
		.AddSingleton<IAnalysisMetricsCollector, AnalysisMetricsCollector>()
		.AddSingleton<IAnalysisScopeResolver, AnalysisScopeResolver>()
		.AddSingleton<IAnalysisResultOrderer, AnalysisResultOrderer>()
		.AddSingleton<ISymbolLookupService, SymbolLookupService>()
		.AddSingleton<IReferenceSearchService, ReferenceSearchService>()
		.AddSingleton<ICallGraphService, CallGraphService>()
		.AddSingleton<ITypeIntrospectionService, TypeIntrospectionService>()
		.AddSingleton<INavigationService, RoslynNavigationService>()
		.AddSingleton<IRefactoringOperationOrchestrator, RefactoringOperationOrchestrator>()
		.AddSingleton<RoslynRefactoringService>(provider =>
			new RoslynRefactoringService(provider.GetRequiredService<IRefactoringOperationOrchestrator>()))
		.AddSingleton<IRefactoringService>(p => p.GetRequiredService<RoslynRefactoringService>())
		.AddSingleton<RoslynAnalysisService>(provider =>
			new RoslynAnalysisService(
				provider.GetRequiredService<IRoslynSolutionAccessor>(),
				provider.GetRequiredService<IAnalysisDiagnosticsRunner>(),
				provider.GetRequiredService<IAnalysisMetricsCollector>(),
				provider.GetRequiredService<IAnalysisScopeResolver>(),
				provider.GetRequiredService<IAnalysisResultOrderer>(),
				provider.GetService<Microsoft.Extensions.Logging.ILogger<RoslynAnalysisService>>()))
		.AddSingleton<IAnalysisService>(p => p.GetRequiredService<RoslynAnalysisService>())
		.AddSingleton<IWorkspaceBootstrapService, WorkspaceBootstrapService>()
		.AddSingleton<ICodeUnderstandingService, CodeUnderstandingService>()
		.AddSingleton<IFlowTraceService, FlowTraceService>()
		.AddSingleton<ICodeSmellFindingService, CodeSmellFindingService>();
}
