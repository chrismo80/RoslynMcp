using RoslynMcp.Infrastructure;
using RoslynMcp.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RoslynMcp.McpServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRoslynMcpMcpServer(this IServiceCollection services)
    {
        services.AddRoslynMcpInfrastructure();

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        var builder = services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "RoslynMcp",
                Version = "0.1.0"
            };
        });

        builder.WithStdioServerTransport()
            .WithTools<LoadSolutionTools>(serializerOptions)
            .WithTools<UnderstandCodebaseTools>(serializerOptions)
            .WithTools<ExplainSymbolTools>(serializerOptions)
            .WithTools<ListTypesTools>(serializerOptions)
            .WithTools<ListMembersTools>(serializerOptions)
            .WithTools<ResolveSymbolTools>(serializerOptions)
            .WithTools<TraceFlowTools>(serializerOptions)
            .WithTools<CodeSmellTools>(serializerOptions)
            .WithTools<ListDependenciesTools>(serializerOptions)
            .WithTools<FindUsagesTools>(serializerOptions);

        return services
            .AddSingleton<LoadSolutionTools>()
            .AddSingleton<UnderstandCodebaseTools>()
            .AddSingleton<ExplainSymbolTools>()
            .AddSingleton<ListTypesTools>()
            .AddSingleton<ListMembersTools>()
            .AddSingleton<ResolveSymbolTools>()
            .AddSingleton<TraceFlowTools>()
            .AddSingleton<CodeSmellTools>()
            .AddSingleton<ListDependenciesTools>()
            .AddSingleton<FindUsagesTools>();
    }
}