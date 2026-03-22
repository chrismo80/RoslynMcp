namespace RoslynMcp.Tools.Inspection.FindCodeSmells;

public sealed record Request(
    string Path,
    int? MaxFindings = null,
    IReadOnlyList<string>? RiskLevels = null,
    IReadOnlyList<string>? Categories = null,
    string? ReviewMode = null);

public sealed record Result(
    CodeSmellsSummary Summary,
    IReadOnlyList<CodeSmellFindingEntry> Findings,
    IReadOnlyList<string>? Warnings,
    Context Context,
    ErrorInfo? Error = null);

public sealed record CodeSmellsSummary(
    int TotalFindings,
    int TotalOccurrences);

public sealed record CodeSmellOccurrenceFile(
    string FilePath,
    IReadOnlyList<ReferencePosition> Locations);

public sealed record CodeSmellFindingEntry(
    string FindingKey,
    string Title,
    string RiskLevel,
    string Category,
    string ReviewKind,
    int OccurrenceCount,
    IReadOnlyList<CodeSmellOccurrenceFile> OccurrenceFiles);

public sealed record ReferencePosition(
    int Line,
    int Column);

public sealed record Context(
    string SourceBias,
    string ResultCompleteness,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> DegradedReasons,
    string? RecommendedNextStep = null);

public sealed record ErrorInfo(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);

public static class ReviewModes
{
    public const string Default = "default";
    public const string Conservative = "conservative";
}

public static class ReviewKinds
{
    public const string StyleSuggestion = "style_suggestion";
    public const string CodeFixHint = "code_fix_hint";
    public const string ReviewConcern = "review_concern";
}

internal static class SourceBiases
{
    public const string Handwritten = "handwritten";
    public const string Generated = "generated";
    public const string Mixed = "mixed";
    public const string Unknown = "unknown";
}

internal static class CompletenessStates
{
    public const string Complete = "complete";
    public const string Partial = "partial";
    public const string Degraded = "degraded";
}

internal sealed record SourceLocation(string FilePath, int Line, int Column);
internal sealed record Match(string Title, string Category, SourceLocation Location, string Origin, string RiskLevel, string ReviewKind);
internal sealed record Filters(string Path, int? MaxFindings, HashSet<string>? RiskLevels, HashSet<string>? Categories, string ReviewMode)
{
    public bool Accepts(Match match)
    {
        if (RiskLevels?.Contains(match.RiskLevel) == false)
            return false;

        if (Categories?.Contains(match.Category) == false)
            return false;

        return true;
    }
}
