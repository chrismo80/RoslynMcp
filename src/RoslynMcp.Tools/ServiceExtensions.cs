using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.LoadSolution;
using RoslynMcp.Tools.Inspection.UnderstandProjects;
using WorkspaceService = RoslynMcp.Tools.Workspace.Service;

namespace RoslynMcp.Tools;

public static class ServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection WithRoslynMcp() => services
            .AddInfrastructure()
            .AddTools();

        private IServiceCollection AddInfrastructure() => services
            .AddSingleton<WorkspaceService>();

        private IServiceCollection AddTools() => services
            .AddLoadSolutionTool()
            .AddUnderstandProjectsTool();
    }
}
