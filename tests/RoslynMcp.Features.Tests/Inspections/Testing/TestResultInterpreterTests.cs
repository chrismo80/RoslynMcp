using System.Reflection;
using Is.Assertions;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure;
using Xunit;

namespace RoslynMcp.Features.Tests.Inspections.Testing;

public sealed class TestResultInterpreterTests
{
    [Fact]
    public void Interpret_WhenFilterMatchesNoTests_ReturnsPassedWithSummary()
    {
        var assembly = typeof(InfrastructureExtensions).Assembly;
        var interpreterType = assembly.GetType("RoslynMcp.Infrastructure.Testing.TestResultInterpreter", throwOnError: true)!;
        var processResultType = assembly.GetType("RoslynMcp.Infrastructure.Testing.TestProcessResult", throwOnError: true)!;
        var interpreter = Activator.CreateInstance(interpreterType, nonPublic: true)!;
        var processResult = Activator.CreateInstance(
            processResultType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                1,
                "No test matches the given testcase filter `FullyQualifiedName=Missing.Tests` in /tmp/run.",
                string.Empty,
                "FullyQualifiedName=Missing.Tests"
            ],
            culture: null)!;

        var result = (RunTestsResult)interpreterType.GetMethod("Interpret")!.Invoke(interpreter, [processResult, Array.Empty<string>(), Array.Empty<string>()])!;

        result.Outcome.Is(RunTestOutcomes.Passed);
        result.Failures.Count.Is(0);
        result.BuildDiagnostics.IsNull();
        result.Summary.Is("No tests matched the filter.");
    }

    [Fact]
    public void Interpret_WhenOutputShowsInvalidFilter_ReturnsInfrastructureError()
    {
        var assembly = typeof(InfrastructureExtensions).Assembly;
        var interpreterType = assembly.GetType("RoslynMcp.Infrastructure.Testing.TestResultInterpreter", throwOnError: true)!;
        var processResultType = assembly.GetType("RoslynMcp.Infrastructure.Testing.TestProcessResult", throwOnError: true)!;
        var interpreter = Activator.CreateInstance(interpreterType, nonPublic: true)!;
        var processResult = Activator.CreateInstance(
            processResultType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                1,
                string.Empty,
                "Invalid format for TestCaseFilter Error: Missing closing parenthesis ')'",
                "FullyQualifiedName=("
            ],
            culture: null)!;

        var result = (RunTestsResult)interpreterType.GetMethod("Interpret")!.Invoke(interpreter, [processResult, Array.Empty<string>(), Array.Empty<string>()])!;

        result.Outcome.Is(RunTestOutcomes.InfrastructureError);
        result.Failures.Count.Is(0);
        result.BuildDiagnostics.IsNull();
        result.Summary!.ShouldNotBeEmpty();
        result.Summary!.Contains("filter", StringComparison.OrdinalIgnoreCase).IsTrue();
    }
}
