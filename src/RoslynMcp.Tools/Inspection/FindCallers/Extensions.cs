using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Inspection.FindCallers;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddFindCallersTool() => services
            .AddSingleton<Service>()
            .AddSingleton<Tool>();
    }
}
