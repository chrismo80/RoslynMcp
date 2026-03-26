using System.Collections.ObjectModel;
using System.Diagnostics;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.RunTests;

internal static partial class DotNet
{
    public static async Task<Result> Test(WorkspaceManager workspaceManager, string targetPath, string? filter, CancellationToken cancellationToken)
    {
        var resultsDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcp", Guid.NewGuid().ToString("N"));
        
        using var runner = new TestRunner(workspaceManager, targetPath, filter, resultsDirectory);
        
        return await runner.Run(cancellationToken);
    }
}

internal sealed class TestRunner(
    WorkspaceManager workspaceManager,
    string targetPath,
    string? filter,
    string resultsDirectory
    )
    : ProcessRunner("dotnet")
{
    internal async Task<Result> Run(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(resultsDirectory);
        
        var processResult = await Run(targetPath, cancellationToken).ConfigureAwait(false);
            
        var trxReports = resultsDirectory.DiscoverFiles("*.trx").ToList();
            
        var trxRun = ResultInterpreter.ParseTrxRun(trxReports, workspaceManager);
            
        return ResultInterpreter.Interpret(processResult, trxRun);
    }

    protected override void SetArguments(Collection<string> arguments)
    {
        arguments.Add("test");
        arguments.Add(targetPath);
        arguments.Add("--nologo");
        arguments.Add("--verbosity");
        arguments.Add("minimal");
        arguments.Add("--logger");
        arguments.Add("trx");
        arguments.Add("--results-directory");
        arguments.Add(resultsDirectory);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            arguments.Add("--filter");
            arguments.Add(filter.Trim());
        }
    }

    protected override void PrepareEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment.Remove("MSBuildSDKsPath");
        startInfo.Environment.Remove("MSBUILD_EXE_PATH");
        startInfo.Environment.Remove("MSBuildExtensionsPath");
        startInfo.Environment.Remove("MSBuildLoadMicrosoftTargetsReadOnly");
        startInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR");
    }

    protected override void OnDispose()
    {
        if (Directory.Exists(resultsDirectory))
            Directory.Delete(resultsDirectory, recursive: true);
    }
}

