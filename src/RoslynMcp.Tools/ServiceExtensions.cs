using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Inspection.ListTypes;
using RoslynMcp.Tools.Inspection.LoadSolution;
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
            .AddSingleton<Workspace>();

        private IServiceCollection AddTools() => services
            .AddLoadSolutionTool()
            .AddUnderstandProjectsTool()
            .AddListTypesTool();
    }
}
