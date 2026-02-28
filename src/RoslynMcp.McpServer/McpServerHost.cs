using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.McpServer;

public static class McpServerHost
{
    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(consoleOptions =>
        {
            consoleOptions.SingleLine = true;
            consoleOptions.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
        });

        builder.Services.AddRoslynMcpMcpServer();

        var host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
