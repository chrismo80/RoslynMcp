using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using RoslynMcp.Features;
using RoslynMcp.Infrastructure;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RoslynMcp.Features.Tools;

namespace RoslynMcp.Host;

public static class HostExtensions
{
    public static void Compose(this IServiceCollection services) => services
        .AddInfrastructure()
        .AddImplementations<Features.Tool>()
        .AddMcpRuntime();

    private static IServiceCollection AddMcpRuntime(this IServiceCollection services)
    {
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            WriteIndented = true
        };

        var builder = services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "RoslynMcp",
                Version = "0.1.0"
            };
        });

        builder.WithStdioServerTransport();
        builder.WithTools(FeatureExtensions.GetImplementations<Features.Tool>(), serializerOptions);

        services.AddSingleton<HostRuntime>();
        services.AddHostedService(provider => provider.GetRequiredService<HostRuntime>());

        return services;
    }
}
