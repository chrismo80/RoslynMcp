using Is.Assertions;
using RoslynMcp.Tools.Tests.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

[Collection(CurrentDirectorySensitiveCollection.Name)]
public sealed class RunTestsToolTests(ITestOutputHelper output)
    : IsolatedToolTests<RoslynMcp.Tools.Inspection.RunTests.Tool>(output)
{
    private const string RunTestsFixturesDirectoryName = "RunTestsFixtures";
    private const string PassingOnlyProjectName = "PassingOnlyTests";
    private const string MixedOutcomeProjectName = "MixedOutcomeTests";
    private const string BrokenBuildProjectName = "BrokenBuildTests";

    [Fact]
    public async Task Run_WithoutTarget_UsesLoadedSolutionAndFindsIntegratedFixtureProjects()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var result = await sut.Run(CancellationToken.None);

        result.Error.IsNull();
        result.Outcome.Is("test_failures");
        result.FailureGroups.SelectMany(static group => group.Failures).Select(static failure => failure.TestName).OrderBy(static name => name, StringComparer.Ordinal).ToArray()
            .Is(
                "RunTestsFixtures.MixedOutcomes.MixedOutcomeTests.Async_failure_test",
                "RunTestsFixtures.MixedOutcomes.MixedOutcomeTests.Theory_failure_test(expected: 2, actual: 3)",
                "RunTestsFixtures.MixedOutcomes.MixedOutcomeTests.Trx_failure_test",
                "RunTestsFixtures.MultiProjectFailures.FirstSolutionFailureTests.First_failing_test",
                "RunTestsFixtures.MultiProjectFailures.SecondSolutionFailureTests.Second_failing_test");
        result.Counts.IsNotNull();
        result.Counts!.Total.Is(7);
        result.Counts.Executed.Is(7);
        result.Counts.Passed.Is(2);
        result.Counts.Failed.Is(5);
    }

    [Fact]
    public async Task Run_WithSolutionTarget_RunsSelectedSolutionAndReturnsPassed()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var result = await sut.Run(CancellationToken.None, GetFixtureSolutionPath(context, "RunTestsPassingOnly.sln"));

        result.Error.IsNull();
        result.Outcome.Is("passed");
        result.FailureGroups.Count.Is(0);
        result.ExitCode.Is(0);
        result.Counts.IsNotNull();
        result.Counts!.Total.Is(1);
        result.Counts.Executed.Is(1);
        result.Counts.Passed.Is(1);
    }

    [Fact]
    public async Task Run_WithProjectTarget_OnlyRunsTargetedProject()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var result = await sut.Run(CancellationToken.None, GetFixtureProjectPath(context, PassingOnlyProjectName));

        result.Error.IsNull();
        result.Outcome.Is("passed");
        result.FailureGroups.Count.Is(0);
        result.Counts.IsNotNull();
        result.Counts!.Total.Is(1);
        result.Counts.Executed.Is(1);
        result.Counts.Passed.Is(1);
    }

    [Fact]
    public async Task Run_WithFilter_NarrowsExecutedTests()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var result = await sut.Run(CancellationToken.None, GetFixtureProjectPath(context, MixedOutcomeProjectName), "FullyQualifiedName=RunTestsFixtures.MixedOutcomes.MixedOutcomeTests.Passing_filter_test");

        result.Error.IsNull();
        result.Outcome.Is("passed");
        result.FailureGroups.Count.Is(0);
        result.Counts.IsNotNull();
        result.Counts!.Total.Is(1);
        result.Counts.Executed.Is(1);
        result.Counts.Passed.Is(1);
    }

    [Fact]
    public async Task Run_WhenFilterMatchesNoTests_ReturnsPassedWithInformativeSummary()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var result = await sut.Run(CancellationToken.None, GetFixtureProjectPath(context, PassingOnlyProjectName), "FullyQualifiedName=RunTestsFixtures.PassingOnly.PassingOnlyTests.Missing_test");

        result.Error.IsNull();
        result.Outcome.Is("passed");
        result.FailureGroups.Count.Is(0);
        result.Summary.Is("No tests matched the filter.");
    }

    [Fact]
    public async Task Run_UsesTrxAsSingleResultSourceAndReturnsStructuredFailureData()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var result = await sut.Run(CancellationToken.None, GetFixtureProjectPath(context, MixedOutcomeProjectName), "FullyQualifiedName=RunTestsFixtures.MixedOutcomes.MixedOutcomeTests.Trx_failure_test");

        result.Error.IsNull();
        result.Outcome.Is("test_failures");
        result.FailureGroups.Count.Is(1);
        result.FailureGroups[0].File!.ShouldNotBeEmpty();
        result.FailureGroups[0].Count.Is(1);
        result.FailureGroups[0].Failures[0].TestName.Is("RunTestsFixtures.MixedOutcomes.MixedOutcomeTests.Trx_failure_test");
        result.FailureGroups[0].Failures[0].Message!.ShouldNotBeEmpty();
        result.FailureGroups[0].Failures[0].Line.IsNotNull();
        result.Counts.IsNotNull();
        result.Counts!.Total.Is(1);
        result.Counts.Executed.Is(1);
        result.Counts.Passed.Is(0);
        result.Counts.Failed.Is(1);
    }

    [Fact]
    public async Task Run_WithSolutionTarget_AggregatesTrxFailuresAcrossProjects()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var result = await sut.Run(CancellationToken.None, GetFixtureSolutionPath(context, "RunTestsMultiProjectFailures.sln"));

        result.Error.IsNull();
        result.Outcome.Is("test_failures");
        result.FailureGroups.Count.Is(2);
        result.FailureGroups.SelectMany(static group => group.Failures).Select(static failure => failure.TestName).OrderBy(static name => name, StringComparer.Ordinal).ToArray()
            .Is(
                "RunTestsFixtures.MultiProjectFailures.FirstSolutionFailureTests.First_failing_test",
                "RunTestsFixtures.MultiProjectFailures.SecondSolutionFailureTests.Second_failing_test");
        result.FailureGroups.All(static group => group.Count == 1).IsTrue();
        result.Counts!.Total.Is(2);
        result.Counts.Executed.Is(2);
        result.Counts.Failed.Is(2);
    }

    [Fact]
    public async Task Run_WhenAsyncTestFails_UsesTrxIdentityInsteadOfMoveNextArtifacts()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var result = await sut.Run(CancellationToken.None, GetFixtureProjectPath(context, MixedOutcomeProjectName), "FullyQualifiedName=RunTestsFixtures.MixedOutcomes.MixedOutcomeTests.Async_failure_test");

        result.Error.IsNull();
        result.Outcome.Is("test_failures");
        result.FailureGroups.Count.Is(1);
        result.FailureGroups[0].Failures[0].TestName.Is("RunTestsFixtures.MixedOutcomes.MixedOutcomeTests.Async_failure_test");
        result.FailureGroups[0].Failures[0].TestName!.Contains("MoveNext", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task Run_WhenTheoryTestFails_UsesTheoryFailureIdentityFromTrx()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var result = await sut.Run(CancellationToken.None, GetFixtureProjectPath(context, MixedOutcomeProjectName), "FullyQualifiedName~RunTestsFixtures.MixedOutcomes.MixedOutcomeTests.Theory_failure_test");

        result.Error.IsNull();
        result.Outcome.Is("test_failures");
        result.FailureGroups.Count.Is(1);
        result.FailureGroups[0].Failures[0].TestName!.Contains("Theory_failure_test", StringComparison.Ordinal).IsTrue();
        result.FailureGroups[0].Failures[0].TestName!.Contains("MoveNext", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task Run_WithWorkspaceRelativeProjectTarget_RunsTargetedProject()
    {
        await using var context = await WorkspaceRootSandboxContext.CreateAsync();
        var sut = context.GetRequiredService<RoslynMcp.Tools.Inspection.RunTests.Tool>();
        var result = await sut.Run(CancellationToken.None, Path.Combine(RunTestsFixturesDirectoryName, PassingOnlyProjectName, $"{PassingOnlyProjectName}.csproj"));

        result.Error.IsNull();
        result.Outcome.Is("passed");
        result.FailureGroups.Count.Is(0);
    }

    [Fact]
    public async Task Run_WithWorkspaceRelativeProjectTarget_ForNestedSolution_ResolvesFromWorkspaceRoot()
    {
        await using var context = await NestedWorkspaceRootSandboxContext.CreateAsync();
        var sut = context.GetRequiredService<RoslynMcp.Tools.Inspection.RunTests.Tool>();
        var relativeTarget = Path.Combine("tests", "TestSolution", RunTestsFixturesDirectoryName, PassingOnlyProjectName, $"{PassingOnlyProjectName}.csproj");

        var result = await sut.Run(CancellationToken.None, relativeTarget);

        result.Error.IsNull();
        result.Outcome.Is("passed");
        result.FailureGroups.Count.Is(0);
        result.Counts.IsNotNull();
        result.Counts!.Passed.Is(1);
    }

    [Fact]
    public async Task Run_WhenBuildFails_ReturnsBuildDiagnostics()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var result = await sut.Run(CancellationToken.None, GetFixtureProjectPath(context, BrokenBuildProjectName));

        result.Error.IsNull();
        result.Outcome.Is("build_failed");
        result.FailureGroups.Count.Is(0);
        result.BuildDiagnostics.IsNotNull();
        result.BuildDiagnostics!.Count.IsGreaterThan(0);
        result.BuildDiagnostics[0].Severity.Is("error");
        result.BuildDiagnostics[0].File!.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Run_IgnoresStaleFailureReportFilesFromEarlierRuns()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var projectPath = GetFixtureProjectPath(context, PassingOnlyProjectName);
        var staleReportPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "FailureReport.json");
        await File.WriteAllTextAsync(staleReportPath, "[{\"Message\":\"stale\",\"Method\":\"ShouldNotAppear\"}]");
        File.SetLastWriteTimeUtc(staleReportPath, DateTime.UtcNow.AddMinutes(-10));

        var result = await sut.Run(CancellationToken.None, projectPath);

        result.Error.IsNull();
        result.Outcome.Is("passed");
        result.FailureGroups.Count.Is(0);
    }

    [Fact]
    public async Task Run_WhenTargetIsOutsideLoadedSolutionDirectory_ReturnsInvalidInputError()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var outsideDirectory = Path.Combine(Path.GetTempPath(), $"RoslynMcpRunTestsOutside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDirectory);

        try
        {
            var result = await sut.Run(CancellationToken.None, outsideDirectory);

            result.Outcome.Is("infrastructure_error");
            result.FailureGroups.Count.Is(0);
            result.Error.IsNotNull();
            result.Error!.Code.Is("invalid_input");
            result.Error.Message.Contains("inside the loaded solution directory", StringComparison.OrdinalIgnoreCase).IsTrue();
            result.Summary!.Contains("inside the loaded solution directory", StringComparison.OrdinalIgnoreCase).IsTrue();
        }
        finally
        {
            if (Directory.Exists(outsideDirectory))
                Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    private static string GetFixtureProjectPath(IsolatedSandboxContext context, string projectName)
        => Path.Combine(context.TestSolutionDirectory, RunTestsFixturesDirectoryName, projectName, $"{projectName}.csproj");

    private static string GetFixtureSolutionPath(IsolatedSandboxContext context, string solutionFileName)
        => Path.Combine(context.TestSolutionDirectory, RunTestsFixturesDirectoryName, solutionFileName);

    private sealed class WorkspaceRootSandboxContext : SandboxContext
    {
        public static async Task<WorkspaceRootSandboxContext> CreateAsync(CancellationToken cancellationToken = default)
        {
            var context = new WorkspaceRootSandboxContext();
            try
            {
                var sandbox = TestSolutionSandbox.Create(context.CanonicalTestSolutionDirectory);
                using var currentDirectory = new CurrentDirectoryScope(sandbox.SolutionRoot);
                await context.InitializeSandboxAsync(sandbox, cancellationToken).ConfigureAwait(false);
                return context;
            }
            catch
            {
                await context.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private sealed class NestedWorkspaceRootSandboxContext : SandboxContext
    {
        public static async Task<NestedWorkspaceRootSandboxContext> CreateAsync(CancellationToken cancellationToken = default)
        {
            var context = new NestedWorkspaceRootSandboxContext();
            try
            {
                var sandbox = TestSolutionSandbox.CreateNested(context.CanonicalTestSolutionDirectory, "tests", "TestSolution");
                using var currentDirectory = new CurrentDirectoryScope(sandbox.SandboxRoot);
                await context.InitializeSandboxAsync(sandbox, cancellationToken).ConfigureAwait(false);
                return context;
            }
            catch
            {
                await context.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _originalDirectory;

        public CurrentDirectoryScope(string currentDirectory)
        {
            _originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(currentDirectory);
        }

        public void Dispose()
            => Directory.SetCurrentDirectory(Directory.Exists(_originalDirectory) ? _originalDirectory : AppContext.BaseDirectory);
    }
}
