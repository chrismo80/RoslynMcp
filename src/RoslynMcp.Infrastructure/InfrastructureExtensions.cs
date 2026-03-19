using RoslynMcp.Core.Contracts;
using RoslynMcp.Infrastructure.Agent;
using RoslynMcp.Infrastructure.Documentation;
using RoslynMcp.Infrastructure.Analysis;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Refactoring;
using RoslynMcp.Infrastructure.Testing;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Infrastructure;

public static class InfrastructureExtensions
{
    extension(IServiceCollection services)
    {
	    public IServiceCollection AddInfrastructure() => services
		    .AddSingleton<ISessionStateStore, SessionStateStore>()
		    .AddSingleton<IWorkspaceRootDiscovery, WorkspaceRootDiscovery>()
		    .AddSingleton<ISolutionPathResolver, SolutionPathResolver>()
		    .AddSingleton<IMSBuildRegistrationGate, MsBuildRegistrationGate>()
		    .AddSingleton<ISessionWorkspaceLoader, SessionWorkspaceLoader>()
		    .AddSingleton<RoslynSolutionSessionService>()
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
		    .AddSingleton<ISymbolDocumentationProvider, RoslynSymbolDocumentationProvider>()
		    .AddSingleton<IRefactoringOperationOrchestrator, RefactoringOperationOrchestrator>()
		    .AddSingleton<IRefactoringService, RoslynRefactoringService>()
		    .AddSingleton<IAnalysisService, RoslynAnalysisService>()
		    .AddSingleton<IWorkspaceBootstrapService, WorkspaceBootstrapService>()
		    .AddSingleton<ICodeUnderstandingService, CodeUnderstandingService>()
		    .AddSingleton<IFlowTraceService, FlowTraceService>()
		    .AddSingleton<ICodeSmellFindingService, CodeSmellFindingService>()
		    .AddSingleton<ITestProcessRunner, TestProcessRunner>()
		    .AddSingleton<ITestResultInterpreter, TestResultInterpreter>()
		    .AddSingleton<ITestInspectionService, TestInspectionService>();

	    public IServiceCollection AddInterfacesOf<T>() where T : class
	    {
		    services.AddSingleton(provider => ActivatorUtilities.CreateInstance<T>(provider));

		    foreach (var service in typeof(T).GetInterfaces())
		    {
			    if (services.Any(s => s.ServiceType == service))
				    throw new ArgumentException($"{service} already registered!");

			    services.AddSingleton(service, provider => provider.GetRequiredService<T>());
		    }

		    return services;
	    }
    }
}
