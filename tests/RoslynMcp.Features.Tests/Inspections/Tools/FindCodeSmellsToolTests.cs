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
        ShouldMatchFindings(result.Actions, BaselineFindings);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithMaxFindings_LimitsReturnedResults()
    {
        var limited = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, maxFindings: 3);

        limited.Error.ShouldBeNone();
        ShouldMatchFindings(limited.Actions, BaselineFindings[..3]);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithRiskLevels_FiltersAcceptedFindings()
    {
        var filtered = await Sut.ExecuteAsync(
            CancellationToken.None,
            CodeSmellsPath,
            riskLevels: ["blocked"]);

        filtered.Error.ShouldBeNone();
        ShouldMatchFindings(filtered.Actions, BlockedFindings);
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

    private static readonly ExpectedCodeSmellFinding[] BaselineFindings =
    [
        new(7, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(8, 30, "Remove '_unusedField'", "roslynator", "blocked"),
        new(9, 1, "Remove trailing white-space", "roslynator", "blocked"),
        new(10, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(21, 1, "Remove trailing white-space", "roslynator", "blocked"),
        new(22, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(25, 16, "Parenthesize 'value * 42'", "roslynator", "blocked"),
        new(27, 1, "Remove trailing white-space", "roslynator", "blocked"),
        new(28, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(29, 29, "Diagnostic: RCS1163", "analyzer", "info"),
        new(29, 36, "Diagnostic: RCS1163", "analyzer", "info"),
        new(29, 43, "Diagnostic: RCS1163", "analyzer", "info"),
        new(29, 50, "Diagnostic: RCS1163", "analyzer", "info"),
        new(29, 57, "Diagnostic: RCS1163", "analyzer", "info"),
        new(29, 64, "Diagnostic: RCS1163", "analyzer", "info"),
        new(32, 1, "Remove trailing white-space", "roslynator", "blocked"),
        new(33, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(37, 9, "Diagnostic: CS0162", "analyzer", "info"),
        new(39, 1, "Remove trailing white-space", "roslynator", "blocked"),
        new(40, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(25, 24, "Parenthesize 'value * 42'", "roslynator", "blocked")
    ];

    private static readonly ExpectedCodeSmellFinding[] AnalyzerFindings =
    [
        new(29, 29, "Diagnostic: RCS1163", "analyzer", "info"),
        new(29, 36, "Diagnostic: RCS1163", "analyzer", "info"),
        new(29, 43, "Diagnostic: RCS1163", "analyzer", "info"),
        new(29, 50, "Diagnostic: RCS1163", "analyzer", "info"),
        new(29, 57, "Diagnostic: RCS1163", "analyzer", "info"),
        new(29, 64, "Diagnostic: RCS1163", "analyzer", "info"),
        new(37, 9, "Diagnostic: CS0162", "analyzer", "info")
    ];

    private static readonly ExpectedCodeSmellFinding[] BlockedFindings =
    [
        new(7, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(8, 30, "Remove '_unusedField'", "roslynator", "blocked"),
        new(9, 1, "Remove trailing white-space", "roslynator", "blocked"),
        new(10, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(21, 1, "Remove trailing white-space", "roslynator", "blocked"),
        new(22, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(25, 16, "Parenthesize 'value * 42'", "roslynator", "blocked"),
        new(27, 1, "Remove trailing white-space", "roslynator", "blocked"),
        new(28, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(32, 1, "Remove trailing white-space", "roslynator", "blocked"),
        new(33, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(39, 1, "Remove trailing white-space", "roslynator", "blocked"),
        new(40, 5, "Convert comment to documentation comment", "roslynator", "blocked"),
        new(25, 24, "Parenthesize 'value * 42'", "roslynator", "blocked")
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