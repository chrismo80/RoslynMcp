using System.Collections.ObjectModel;
using System.Diagnostics;

namespace RoslynMcp.Infrastructure.Testing;

internal interface ITestProcessRunner
{
    Task<TestProcessResult> RunAsync(string targetPath, string resultsDirectory, string? filter, CancellationToken cancellationToken);
}

internal sealed class TestProcessRunner : ITestProcessRunner
{
    public async Task<TestProcessResult> RunAsync(string targetPath, string resultsDirectory, string? filter, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = GetWorkingDirectory(targetPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        AddArguments(startInfo.ArgumentList, targetPath, resultsDirectory, filter);
        PrepareDotnetCliEnvironment(startInfo);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to start dotnet test.", ex);
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

        return new TestProcessResult(
            process.ExitCode,
            await standardOutputTask.ConfigureAwait(false),
            await standardErrorTask.ConfigureAwait(false),
            string.IsNullOrWhiteSpace(filter) ? null : filter.Trim());
    }

    private static string GetWorkingDirectory(string targetPath)
    {
        if (Directory.Exists(targetPath))
        {
            return targetPath;
        }

        return Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
    }

    private static void AddArguments(Collection<string> argumentList, string targetPath, string resultsDirectory, string? filter)
    {
        argumentList.Add("test");
        argumentList.Add(targetPath);
        argumentList.Add("--nologo");
        argumentList.Add("--verbosity");
        argumentList.Add("minimal");
        argumentList.Add("--logger");
        argumentList.Add("trx");
        argumentList.Add("--results-directory");
        argumentList.Add(resultsDirectory);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            argumentList.Add("--filter");
            argumentList.Add(filter.Trim());
        }
    }

    private static void PrepareDotnetCliEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment.Remove("MSBuildSDKsPath");
        startInfo.Environment.Remove("MSBUILD_EXE_PATH");
        startInfo.Environment.Remove("MSBuildExtensionsPath");
        startInfo.Environment.Remove("MSBuildLoadMicrosoftTargetsReadOnly");
        startInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR");
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
        }
    }
}

internal sealed record TestProcessResult(int ExitCode, string StandardOutput, string StandardError, string? AppliedFilter);
