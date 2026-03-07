using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed record ExpectedCodeSmellFinding(int Line, int Column, string Title, string Category, string RiskLevel);

public sealed class FindCodeSmellsToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<FindCodeSmellsTool>(fixture, output)
{
    [Fact]
    public async Task FindCodeSmellsAsync_WithNoOptionalFilters_PreservesCompatibility()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);

        result.Error.ShouldBeNone();
        result.Actions.Any(static action => action.RiskLevel == "blocked").IsFalse();
        result.Actions.All(static action => action.RiskLevel is "low" or "review_required" or "high" or "info").IsTrue();
        result.Actions.GroupBy(static action => (action.Location.Line, action.Title, action.Category, action.RiskLevel)).All(static group => group.Count() == 1).IsTrue();
        result.Warnings.Any(static warning => warning.Contains("Deduplicated", StringComparison.Ordinal)).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithMaxFindings_LimitsReturnedResults()
    {
        var limited = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, maxFindings: 3);

        limited.Error.ShouldBeNone();
        (limited.Actions.Count <= 3).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithRiskLevels_FiltersAcceptedFindings()
    {
        var filtered = await Sut.ExecuteAsync(
            CancellationToken.None,
            CodeSmellsPath,
            riskLevels: ["high"]);

        filtered.Error.ShouldBeNone();
        filtered.Actions.All(static action => action.RiskLevel == "high").IsTrue();
        filtered.Actions.Count.IsGreaterThan(0);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithCategories_FiltersAcceptedFindings()
    {
        var filtered = await Sut.ExecuteAsync(
            CancellationToken.None,
            CodeSmellsPath,
            categories: ["analyzer"]);

        filtered.Error.ShouldBeNone();
        ShouldMatchFindings(filtered.Actions, AnalyzerFindings);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithEmptyPath_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, string.Empty);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithInvalidMaxFindings_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, maxFindings: 0);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    private static readonly ExpectedCodeSmellFinding[] AnalyzerFindings =
    [
        new(29, 29, "Diagnostic: RCS1163", "analyzer", "info"),
        new(37, 9, "Diagnostic: CS0162", "analyzer", "info")
    ];

    private static void ShouldMatchFindings(IReadOnlyList<CodeSmellMatch> actual, ExpectedCodeSmellFinding[] expected)
    {
        actual.Count.Is(expected.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            var actualFinding = actual[i];
            var expectedFinding = expected[i];

            actualFinding.Location.Line.Is(expectedFinding.Line);
            actualFinding.Location.Column.Is(expectedFinding.Column);
            actualFinding.Title.Is(expectedFinding.Title);
            actualFinding.Category.Is(expectedFinding.Category);
            actualFinding.RiskLevel.Is(expectedFinding.RiskLevel);
        }
    }
}
