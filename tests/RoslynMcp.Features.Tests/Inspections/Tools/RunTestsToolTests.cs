using System.Diagnostics;
using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tests.Mutations;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class RunTestsToolTests(ITestOutputHelper output)
    : IsolatedToolTests<RunTestsTool>(output)
{
    [Fact]
    public async Task ExecuteAsync_WithoutTarget_RunsLoadedSolutionAndReturnsPassed()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        await AddTestProjectAsync(context, new TestProjectSpec(
            "PassingSolutionTests",
            false,
            """
            using Xunit;

            namespace RunTestsSandbox.PassingSolution;

            public sealed class PassingSolutionTests
            {
                [Fact]
                public void Passing_test()
                {
                    Assert.True(true);
                }
            }
            """));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldBeNone();
        result.Outcome.Is(RunTestOutcomes.Passed);
        result.Failures.Count.Is(0);
        result.ExitCode.Is(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithProjectTarget_OnlyRunsTargetedProject()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var passingProject = await AddTestProjectAsync(context, new TestProjectSpec(
            "PassingTargetTests",
            false,
            """
            using Xunit;

            namespace RunTestsSandbox.ProjectTargeting;

            public sealed class PassingTargetTests
            {
                [Fact]
                public void Passing_test()
                {
                    Assert.True(true);
                }
            }
            """));

        await AddTestProjectAsync(context, new TestProjectSpec(
            "FailingNonTargetTests",
            false,
            """
            using Xunit;

            namespace RunTestsSandbox.ProjectTargeting;

            public sealed class FailingNonTargetTests
            {
                [Fact]
                public void Failing_test()
                {
                    Assert.True(false);
                }
            }
            """));

        var result = await sut.ExecuteAsync(CancellationToken.None, passingProject);

        result.Error.ShouldBeNone();
        result.Outcome.Is(RunTestOutcomes.Passed);
        result.Failures.Count.Is(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithFilter_NarrowsExecutedTests()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var projectPath = await AddTestProjectAsync(context, new TestProjectSpec(
            "FilterTests",
            false,
            """
            using Xunit;

            namespace RunTestsSandbox.Filtering;

            public sealed class FilterTests
            {
                [Fact]
                public void Passing_filter_test()
                {
                    Assert.True(true);
                }

                [Fact]
                public void Failing_filter_test()
                {
                    Assert.True(false);
                }
            }
            """));

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            projectPath,
            "FullyQualifiedName=RunTestsSandbox.Filtering.FilterTests.Passing_filter_test");

        result.Error.ShouldBeNone();
        result.Outcome.Is(RunTestOutcomes.Passed);
        result.Failures.Count.Is(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFilterMatchesNoTests_ReturnsPassedWithInformativeSummary()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var projectPath = await AddTestProjectAsync(context, new TestProjectSpec(
            "ZeroMatchFilterTests",
            false,
            """
            using Xunit;

            namespace RunTestsSandbox.ZeroMatch;

            public sealed class ZeroMatchFilterTests
            {
                [Fact]
                public void Existing_test()
                {
                    Assert.True(true);
                }
            }
            """));

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            projectPath,
            "FullyQualifiedName=RunTestsSandbox.ZeroMatch.ZeroMatchFilterTests.Missing_test");

        result.Error.ShouldBeNone();
        result.Outcome.Is(RunTestOutcomes.Passed);
        result.Failures.Count.Is(0);
        result.Summary.Is("No tests matched the filter.");
    }

    [Fact]
    public async Task ExecuteAsync_PrefersJsonFailureReportsWhenAvailable()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var projectPath = await AddTestProjectAsync(context, new TestProjectSpec(
            "JsonFailureTests",
            true,
            """
            using Is.Assertions;
            using Xunit;

            namespace RunTestsSandbox.JsonFailures;

            public sealed class JsonFailureTests
            {
                [Fact]
                public void Json_failure_test()
                {
                    var actual = "actual";
                    actual.Is("expected");
                }
            }
            """));

        var result = await sut.ExecuteAsync(CancellationToken.None, projectPath);

        result.Error.ShouldBeNone();
        result.Outcome.Is(RunTestOutcomes.TestFailures);
        result.Failures.Count.Is(1);
        result.Failures[0].TestName.Is("Json_failure_test");
        result.Failures[0].Expected.Is("expected");
        result.Failures[0].Actual.Is("actual");
        result.Failures[0].File!.ShouldNotBeEmpty();
        result.Failures[0].Code!.ShouldNotBeEmpty();
        result.Failures[0].Line.IsNotNull();
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToTrxWhenJsonFailureReportsAreAbsent()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var projectPath = await AddTestProjectAsync(context, new TestProjectSpec(
            "TrxFallbackTests",
            false,
            """
            using Xunit;

            namespace RunTestsSandbox.TrxFallback;

            public sealed class TrxFallbackTests
            {
                [Fact]
                public void Trx_failure_test()
                {
                    Assert.True(false, "plain xunit failure");
                }
            }
            """));

        var result = await sut.ExecuteAsync(CancellationToken.None, projectPath);

        result.Error.ShouldBeNone();
        result.Outcome.Is(RunTestOutcomes.TestFailures);
        result.Failures.Count.Is(1);
        result.Failures[0].TestName!.EndsWith(".Trx_failure_test", StringComparison.Ordinal).IsTrue();
        result.Failures[0].Message!.ShouldNotBeEmpty();
        result.Failures[0].StackTrace!.ShouldNotBeEmpty();
        result.Failures[0].Expected.IsNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutTarget_AggregatesTrxFailuresAcrossProjects()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        await AddTestProjectAsync(context, new TestProjectSpec(
            "FirstFailingSolutionTests",
            false,
            """
            using Xunit;

            namespace RunTestsSandbox.MultiProjectFailures;

            public sealed class FirstFailingSolutionTests
            {
                [Fact]
                public void First_failing_test()
                {
                    Assert.True(false, "first failure");
                }
            }
            """));

        await AddTestProjectAsync(context, new TestProjectSpec(
            "SecondFailingSolutionTests",
            false,
            """
            using Xunit;

            namespace RunTestsSandbox.MultiProjectFailures;

            public sealed class SecondFailingSolutionTests
            {
                [Fact]
                public void Second_failing_test()
                {
                    Assert.True(false, "second failure");
                }
            }
            """));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldBeNone();
        result.Outcome.Is(RunTestOutcomes.TestFailures);
        result.Failures.Count.Is(2);
        result.Failures.Select(static failure => failure.TestName).OrderBy(static name => name, StringComparer.Ordinal).ToArray()
            .Is(new[]
            {
                "RunTestsSandbox.MultiProjectFailures.FirstFailingSolutionTests.First_failing_test",
                "RunTestsSandbox.MultiProjectFailures.SecondFailingSolutionTests.Second_failing_test"
            });
    }

    [Fact]
    public async Task ExecuteAsync_WhenBuildFails_ReturnsBuildDiagnostics()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var projectPath = await AddTestProjectAsync(context, new TestProjectSpec(
            "BrokenBuildTests",
            false,
            """
            using Xunit;

            namespace RunTestsSandbox.BuildFailures;

            public sealed class BrokenBuildTests
            {
                [Fact]
                public void Broken_test()
                {
                    var value = ;
                }
            }
            """));

        var result = await sut.ExecuteAsync(CancellationToken.None, projectPath);

        result.Error.ShouldBeNone();
        result.Outcome.Is(RunTestOutcomes.BuildFailed);
        result.Failures.Count.Is(0);
        result.BuildDiagnostics.IsNotNull();
        result.BuildDiagnostics!.Count.IsGreaterThan(0);
        result.BuildDiagnostics[0].Severity.Is("error");
        result.BuildDiagnostics[0].File!.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresStaleFailureReportFilesFromEarlierRuns()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var projectPath = await AddTestProjectAsync(context, new TestProjectSpec(
            "StaleReportTests",
            false,
            """
            using Xunit;

            namespace RunTestsSandbox.StaleReports;

            public sealed class StaleReportTests
            {
                [Fact]
                public void Passing_test()
                {
                    Assert.True(true);
                }
            }
            """));

        var staleReportPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "FailureReport.json");
        await File.WriteAllTextAsync(staleReportPath, "[{\"Message\":\"stale\",\"Method\":\"ShouldNotAppear\"}]");
        File.SetLastWriteTimeUtc(staleReportPath, DateTime.UtcNow.AddMinutes(-10));

        var result = await sut.ExecuteAsync(CancellationToken.None, projectPath);

        result.Error.ShouldBeNone();
        result.Outcome.Is(RunTestOutcomes.Passed);
        result.Failures.Count.Is(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetIsOutsideLoadedSolutionDirectory_ReturnsInvalidInputError()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var outsideDirectory = Path.Combine(Path.GetTempPath(), $"RoslynMcpRunTestsOutside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDirectory);

        try
        {
            var result = await sut.ExecuteAsync(CancellationToken.None, outsideDirectory);

            result.Outcome.Is(RunTestOutcomes.InfrastructureError);
            result.Failures.Count.Is(0);
            result.Error.IsNotNull();
            result.Error!.Code.Is(ErrorCodes.InvalidInput);
            result.Error.Message.Contains("inside the loaded solution directory", StringComparison.OrdinalIgnoreCase).IsTrue();
            result.Summary!.Contains("inside the loaded solution directory", StringComparison.OrdinalIgnoreCase).IsTrue();
        }
        finally
        {
            if (Directory.Exists(outsideDirectory))
            {
                Directory.Delete(outsideDirectory, recursive: true);
            }
        }
    }

    private static async Task<string> AddTestProjectAsync(IsolatedSandboxContext context, TestProjectSpec project, CancellationToken cancellationToken = default)
    {
        var projectDirectory = Path.Combine(context.TestSolutionDirectory, project.Name);
        Directory.CreateDirectory(projectDirectory);

        var projectFilePath = Path.Combine(projectDirectory, $"{project.Name}.csproj");
        await File.WriteAllTextAsync(projectFilePath, CreateProjectFile(project.UsesAssertWithIs), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Tests.cs"), project.Source, cancellationToken);

        if (project.UsesAssertWithIs)
        {
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "is.configuration.json"), AssertWithIsConfiguration, cancellationToken);
        }

        await RunDotnetAsync(
            context.TestSolutionDirectory,
            $"sln \"{context.SolutionPath}\" add \"{projectFilePath}\"",
            cancellationToken);

        return projectFilePath;
    }

    private static async Task RunDotnetAsync(string workingDirectory, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        PrepareDotnetCliEnvironment(process.StartInfo);

        process.Start();
        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet {arguments} failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }

    private static string CreateProjectFile(bool usesAssertWithIs)
    {
        var lines = new List<string>
        {
            "<Project Sdk=\"Microsoft.NET.Sdk\">",
            string.Empty,
            "  <PropertyGroup>",
            "    <TargetFramework>net10.0</TargetFramework>",
            "    <ImplicitUsings>enable</ImplicitUsings>",
            "    <Nullable>enable</Nullable>",
            "    <IsPackable>false</IsPackable>",
            "    <IsTestProject>true</IsTestProject>",
            "  </PropertyGroup>",
            string.Empty,
            "  <ItemGroup>"
        };

        if (usesAssertWithIs)
        {
            lines.Add("    <PackageReference Include=\"AssertWithIs\" Version=\"1.10.3\" />");
        }

        lines.Add("    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"18.0.0\" />");
        lines.Add("    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />");
        lines.Add("    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\">");
        lines.Add("      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>");
        lines.Add("      <PrivateAssets>all</PrivateAssets>");
        lines.Add("    </PackageReference>");
        lines.Add("  </ItemGroup>");

        if (usesAssertWithIs)
        {
            lines.Add(string.Empty);
            lines.Add("  <ItemGroup>");
            lines.Add("    <None Remove=\"is.configuration.json\" />");
            lines.Add("    <Content Include=\"is.configuration.json\">");
            lines.Add("      <CopyToOutputDirectory>Always</CopyToOutputDirectory>");
            lines.Add("    </Content>");
            lines.Add("  </ItemGroup>");
        }

        lines.Add("</Project>");
        return string.Join(Environment.NewLine, lines);
    }

    private static void PrepareDotnetCliEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment.Remove("MSBuildSDKsPath");
        startInfo.Environment.Remove("MSBUILD_EXE_PATH");
        startInfo.Environment.Remove("MSBuildExtensionsPath");
        startInfo.Environment.Remove("MSBuildLoadMicrosoftTargetsReadOnly");
        startInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR");
    }

    private const string AssertWithIsConfiguration = """
        {
          "AssertionObserver": "Is.AssertionObservers.JsonObserver, Is",
          "TestAdapter": "Is.TestAdapters.DefaultAdapter, Is",
          "AppendCodeLine": true,
          "ColorizeMessages": false,
          "FloatingPointComparisonPrecision": 1E-06,
          "MaxRecursionDepth": 20,
          "ParsingFlags": 52
        }
        """;

    private sealed record TestProjectSpec(string Name, bool UsesAssertWithIs, string Source);
}
