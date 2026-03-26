using System.Collections.ObjectModel;
using System.Diagnostics;

namespace RoslynMcp.Tools;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal abstract class ProcessRunner(string command) : IDisposable
{
    public async Task<ProcessResult> Run(string workingDirectory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        SetArguments(startInfo.ArgumentList);
        
        PrepareEnvironment(startInfo);

        using var process = new Process();
        
        process.StartInfo = startInfo;
        process.EnableRaisingEvents = true;
        
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to start process.", ex);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            
            throw;
        }

        return new ProcessResult(process.ExitCode, await standardOutputTask.ConfigureAwait(false), await standardErrorTask.ConfigureAwait(false));
    }


    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignored
        }
    }
    
    protected virtual void SetArguments(Collection<string> arguments)
    { }

    protected virtual void PrepareEnvironment(ProcessStartInfo startInfo)
    { }

    protected virtual void OnDispose()
    { }

    public void Dispose()
    {
        OnDispose();
    }
}

