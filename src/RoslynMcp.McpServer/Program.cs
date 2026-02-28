using RoslynMcp.McpServer;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

await McpServerHost.RunAsync(args, cts.Token);
