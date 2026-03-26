using System.Collections.ObjectModel;
using System.Diagnostics;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.RunTests;

internal static partial class DotNet
{
    public static async Task<Result> Test(WorkspaceManager workspaceManager, string? targetPath, string? filter, CancellationToken cancellationToken)
    {
        var resultsDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcp", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(resultsDirectory);

        using var runner = new TestRunner(targetPath, filter, resultsDirectory);

        var workingDirectory = File.Exists(targetPath) switch
        {
            true => Directory.GetParent(targetPath)?.FullName ?? targetPath,
            false => targetPath
        };

        var processResult = await runner.Run(workingDirectory, cancellationToken).ConfigureAwait(false);

        var trxFiles = resultsDirectory.DiscoverFiles("*.trx").ToList();

        var trxRun = ResultInterpreter.ParseTrxRun(trxFiles, workspaceManager);

        return ResultInterpreter.Interpret(processResult, trxRun);
    }
}

internal sealed class TestRunner(string targetPath, string? filter, string resultsDirectory) : ProcessRunner("dotnet")
{
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

        if (string.IsNullOrWhiteSpace(filter))
            return;

        arguments.Add("--filter");
        arguments.Add(filter.Trim());
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