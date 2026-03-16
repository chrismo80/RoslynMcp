using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed record ExpectedCodeSmellFinding(string Title, string Category, string RiskLevel, string ReviewKind, int Line, int Column);

public sealed class FindCodeSmellsToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<FindCodeSmellsTool>(fixture, output)
{
    [Fact]
    public async Task FindCodeSmellsAsync_WithNoOptionalFilters_ReturnsAggregatedContract()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);
        var findings = GetAllFindings(result);

        result.Error.ShouldBeNone();
        result.Summary.TotalFindings.Is(findings.Count);
        result.Summary.TotalOccurrences.Is(findings.Sum(static finding => finding.OccurrenceCount));
        result.Findings.IsNotEmpty();
        findings.All(static finding => finding.RiskLevel is "low" or "review_required" or "high" or "info").IsTrue();
        findings.All(static finding => finding.Category is "analyzer" or "correctness" or "design" or "maintainability" or "performance" or "style").IsTrue();
        findings.All(static finding => finding.ReviewKind is "style_suggestion" or "code_fix_hint" or "review_concern").IsTrue();
        findings.All(static finding => !string.IsNullOrWhiteSpace(finding.FindingKey)).IsTrue();
        result.Warnings!.Any(static warning => warning.Contains("Deduplicated", StringComparison.Ordinal)).IsTrue();
        result.Context.SourceBias.Is(SourceBiases.Handwritten);
        result.Context.ResultCompleteness.Is(ResultCompletenessStates.Complete);
        (result.Warnings?.Any(static warning => warning.Contains("reviewMode=conservative", StringComparison.Ordinal)) ?? false).IsFalse();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_ReturnsRiskBucketsInCanonicalOrder()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);

        result.Error.ShouldBeNone();
        IsCanonicalRiskOrder(result.Findings.Select(static finding => finding.RiskLevel).Distinct()).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_ReturnsCategoriesInCanonicalOrder()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);

        result.Error.ShouldBeNone();
        IsCanonicalCategoryOrder(result.Findings.Select(static finding => finding.Category).Distinct()).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_AggregatesRepeatedFindingsIntoSingleEntryWithOrderedOccurrences()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, GetFilePath("ProjectImpl", "RepeatedCodeSmells"));
        var repeatedFindings = GetAllFindings(result)
            .Where(static finding => finding.Title == "Diagnostic: CS0162")
            .ToArray();

        result.Error.ShouldBeNone();
        repeatedFindings.Length.Is(1);

        var finding = repeatedFindings[0];
        finding.Category.Is("analyzer");
        finding.RiskLevel.Is("info");
        finding.ReviewKind.Is(CodeSmellReviewKinds.CodeFixHint);
        finding.OccurrenceCount.Is(3);
        finding.OccurrenceFiles.Single().Locations.Count.Is(3);
        finding.OccurrenceFiles.Single().Locations.Select(static occurrence => occurrence.Line).ToArray().SequenceEqual([8, 14, 20]).IsTrue();
        IsOccurrenceOrderCanonical(finding.OccurrenceFiles);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithConservativeReviewMode_ReturnsLowerNoiseSubset()
    {
        var defaultResult = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);
        var conservativeResult = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, reviewMode: CodeSmellReviewModes.Conservative);

        defaultResult.Error.ShouldBeNone();
        conservativeResult.Error.ShouldBeNone();
        conservativeResult.Summary.TotalOccurrences.IsGreaterThan(0);
        (conservativeResult.Summary.TotalOccurrences < defaultResult.Summary.TotalOccurrences).IsTrue();
        GetAllFindings(conservativeResult).All(static finding => finding.ReviewKind != CodeSmellReviewKinds.StyleSuggestion).IsTrue();
        conservativeResult.Warnings!.Any(static warning => warning.Contains("reviewMode=conservative", StringComparison.Ordinal)).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithCategories_FiltersAcceptedFindings()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, categories: ["analyzer"]);
        var findings = GetAllFindings(result);

        result.Error.ShouldBeNone();
        findings.Select(static finding => finding.RiskLevel).Distinct().ToArray().SequenceEqual(["info"]).IsTrue();
        findings.Count.Is(AnalyzerFindings.Length);
        findings.All(static finding => finding.Category == "analyzer").IsTrue();
        findings.All(static finding => finding.ReviewKind == CodeSmellReviewKinds.CodeFixHint).IsTrue();

        var findingsByTitle = findings.ToDictionary(static finding => finding.Title, StringComparer.Ordinal);

        foreach (var expected in AnalyzerFindings)
        {
            findingsByTitle.ContainsKey(expected.Title).IsTrue();
            var actual = findingsByTitle[expected.Title];

            actual.Title.Is(expected.Title);
            actual.Category.Is(expected.Category);
            actual.RiskLevel.Is(expected.RiskLevel);
            actual.ReviewKind.Is(expected.ReviewKind);
            actual.OccurrenceCount.Is(1);
            actual.OccurrenceFiles.Single().Locations[0].Line.Is(expected.Line);
            actual.OccurrenceFiles.Single().Locations[0].Column.Is(expected.Column);
        }
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithMaxFindings_LimitsReturnedOccurrences()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, maxFindings: 3);

        result.Error.ShouldBeNone();
        (result.Summary.TotalOccurrences <= 3).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithRiskLevels_FiltersAcceptedFindings()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, riskLevels: ["high"]);
        var findings = GetAllFindings(result);

        result.Error.ShouldBeNone();
        findings.Select(static finding => finding.RiskLevel).Distinct().ToArray().SequenceEqual(["high"]).IsTrue();
        findings.Count.IsGreaterThan(0);
        findings.All(static finding => finding.RiskLevel == "high").IsTrue();
    }

    [Fact]
    public void FindCodeSmellsResult_DoesNotExposeLegacyFlatFields()
    {
        typeof(FindCodeSmellsResult).GetProperty("Actions").IsNull();
        typeof(FindCodeSmellsResult).GetProperty("Groups").IsNull();
        typeof(FindCodeSmellsResult).GetProperty("RiskBuckets").IsNull();
        typeof(CodeSmellFindingEntry).GetProperty("Occurrences").IsNull();
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

    [Fact]
    public async Task FindCodeSmellsAsync_WithUnsupportedCategory_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, categories: ["security"]);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithUnsupportedReviewMode_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, reviewMode: "aggressive");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_Serialization_UsesFlatFindingsContract()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);

        result.Error.ShouldBeNone();

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        json.Contains("riskBuckets", StringComparison.Ordinal).IsFalse();
        json.Contains("occurrences", StringComparison.Ordinal).IsFalse();
        json.Contains("occurrenceFiles", StringComparison.Ordinal).IsTrue();
    }

    private static readonly ExpectedCodeSmellFinding[] AnalyzerFindings =
    [
        new("Diagnostic: RCS1163", "analyzer", "info", CodeSmellReviewKinds.CodeFixHint, 29, 29),
        new("Diagnostic: CS0162", "analyzer", "info", CodeSmellReviewKinds.CodeFixHint, 37, 9)
    ];

    private static List<CodeSmellFindingEntry> GetAllFindings(FindCodeSmellsResult result)
        => result.Findings.ToList();

    private static bool IsCanonicalRiskOrder(IEnumerable<string> values)
    {
        var ordered = values.ToArray();
        return ordered.SequenceEqual(ordered.OrderBy(GetRiskOrder).ThenBy(static value => value, StringComparer.Ordinal));
    }

    private static bool IsCanonicalCategoryOrder(IEnumerable<string> values)
    {
        var ordered = values.ToArray();
        return ordered.SequenceEqual(ordered.OrderBy(GetCategoryOrder).ThenBy(static value => value, StringComparer.Ordinal));
    }

    private static bool IsOccurrenceOrderCanonical(IReadOnlyList<CodeSmellOccurrenceFile> occurrenceFiles)
    {
        var flattened = occurrenceFiles
            .SelectMany(static file => file.Locations.Select(position => new SourceLocation(file.FilePath, position.Line, position.Column)))
            .ToArray();
        var ordered = flattened
            .OrderBy(static occurrence => occurrence.FilePath, StringComparer.Ordinal)
            .ThenBy(static occurrence => occurrence.Line)
            .ThenBy(static occurrence => occurrence.Column)
            .ToArray();

        return flattened.SequenceEqual(ordered);
    }

    private static int GetRiskOrder(string riskLevel)
        => riskLevel switch
        {
            "high" => 0,
            "review_required" => 1,
            "low" => 2,
            "info" => 3,
            _ => 4
        };

    private static int GetCategoryOrder(string category)
        => category switch
        {
            "correctness" => 0,
            "design" => 1,
            "maintainability" => 2,
            "performance" => 3,
            "analyzer" => 4,
            "style" => 5,
            _ => 6
        };
}
