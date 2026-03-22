using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using RoslynMcp.Tools;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RoslynMcp.Host;

public static class HostExtensions
{
    internal static string ServerVersion => Assembly.GetExecutingAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HostExtensions).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    extension(IServiceCollection services)
    {
        public void Compose() => services
            .WithRoslynMcp()
            .AddMcpRuntime();

        private void AddMcpRuntime()
        {
            var serializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                WriteIndented = false
            };

            var builder = services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "RoslynMcp",
                    Version = ServerVersion
                };
            });

            builder.WithStdioServerTransport();
            builder.WithTools(ServiceExtensions.GetTools(), serializerOptions);
        }
    }
}