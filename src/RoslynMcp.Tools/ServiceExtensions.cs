using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.LoadSolution;

namespace RoslynMcp.Tools;

public static class ServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddTools() => services
            .AddLoadSolutionTool();
    }
}