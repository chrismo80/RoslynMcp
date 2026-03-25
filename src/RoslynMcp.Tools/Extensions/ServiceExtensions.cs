using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Extensions;

public static class ServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection WithRoslynMcp() => services
            .AddImplementations<Manager>()
            .AddImplementations<Tool>();
        
        private IServiceCollection AddImplementations<T>()
        {
            foreach (var type in GetImplementations<T>())
                services.AddSingleton(type);
        
            return services;
        }
    }
    
    public static IEnumerable<Type> GetTools() => GetImplementations<Tool>();
    
    private static IEnumerable<Type> GetImplementations<T>() => Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(type => type.Implements<T>())
        .Distinct();
    
    private static bool Implements<T>(this Type type) =>
        type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(T));

}
