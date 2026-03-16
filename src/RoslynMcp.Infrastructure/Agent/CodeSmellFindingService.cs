using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Analysis;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace RoslynMcp.Infrastructure.Agent;

/// <summary>
/// Finds code smells in the codebase using Roslynator analyzers.
/// Aggregates diagnostics by risk level (low, review_required, high) and category.
/// </summary>
public sealed class CodeSmellFindingService(IRoslynSolutionAccessor solutionAccessor, IRefactoringService refactoringService)
    : ICodeSmellFindingService
{
    private const int MaximumScannedAnchors = 500;
    private static readonly string[] SupportedRiskLevels = ["low", "review_required", "high", "info"];
    private static readonly string[] SupportedCategories = ["analyzer", "correctness", "design", "maintainability", "performance", "style"];
    private static readonly string[] SupportedReviewModes = [CodeSmellReviewModes.Default, CodeSmellReviewModes.Conservative];
    private static readonly ResultContextMetadata UnknownContext = new(
        SourceBiases.Unknown,
        ResultCompletenessStates.Degraded,
        Array.Empty<string>(),
        Array.Empty<string>());

    private static readonly CodeSmellsSummary EmptySummary = new(0, 0);

    private readonly IRoslynSolutionAccessor _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));
    private readonly IRefactoringService _refactoringService = refactoringService ?? throw new ArgumentNullException(nameof(refactoringService));
    private readonly RoslynatorAnalyzerCatalog _analyzerCatalog = new();

    public async Task<FindCodeSmellsResult> FindCodeSmellsAsync(FindCodeSmellsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var (filters, validationError) = ValidateRequest(request);
        if (validationError is not null)
            return validationError;

        var path = filters!.Path;

        var (solution, solutionError) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution is null)
        {
            return new FindCodeSmellsResult(
                EmptySummary,
                Array.Empty<CodeSmellFindingEntry>(),
                null,
                UnknownContext,
                AgentErrorInfo.Normalize(solutionError,
                    "Call load_solution first to select a solution before finding code smells."));
        }

        var matchingDocuments = solution.Projects
            .SelectMany(static project => project.Documents)
            .Where(candidate => candidate.FilePath.MatchesByNormalizedPath(path))
            .OrderBy(static candidate => candidate.FilePath, StringComparer.Ordinal)
            .ToArray();

        if (matchingDocuments.Length == 0)
        {
            var error = AgentErrorInfo.Create(
                ErrorCodes.InvalidPath,
                "path did not match any loaded document.",
                "Use a source document path that exists in the loaded solution.",
                ("field", "path"),
                ("provided", path));
            return new FindCodeSmellsResult(EmptySummary, Array.Empty<CodeSmellFindingEntry>(), null, UnknownContext, error);
        }

        if (matchingDocuments.Length > 1)
        {
            var error = AgentErrorInfo.Create(
                ErrorCodes.InvalidPath,
                "path matched multiple loaded documents.",
                "Provide a unique source document path from the loaded solution.",
                ("field", "path"),
                ("provided", path));
            return new FindCodeSmellsResult(EmptySummary, Array.Empty<CodeSmellFindingEntry>(), null, UnknownContext, error);
        }

        var document = matchingDocuments[0];

        var warnings = new List<string>();
        var anchors = await CollectAnchorsAsync(document, warnings, ct).ConfigureAwait(false);
        var actions = new List<CodeSmellMatch>();

        foreach (var anchor in anchors)
        {
            ct.ThrowIfCancellationRequested();

            var discovered = await _refactoringService.GetRefactoringsAtPositionAsync(
                new GetRefactoringsAtPositionRequest(anchor.FilePath, anchor.Line, anchor.Column, null, null, "default"),
                ct).ConfigureAwait(false);

            if (discovered.Error is not null)
            {
                warnings.Add($"Skipped position {anchor.FilePath}:{anchor.Line}:{anchor.Column} ({anchor.AnchorKind}): {discovered.Error.Message}");
                continue;
            }

            var actionsAtAnchor = discovered.Actions
                .OrderBy(action => string.Equals(action.PolicyDecision.Decision, "allow", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(action => MapRisk(action.RiskLevel))
                .ThenBy(action => action.Title, StringComparer.Ordinal)
                .ThenBy(action => action.ActionId, StringComparer.Ordinal)
                .ToArray();

            // If no refactorings found but this is a diagnostic anchor, still create an action
            if (actionsAtAnchor.Length == 0 && anchor.IsDiagnostic && anchor.AnchorKind.StartsWith("Diagnostic:"))
            {
                var diagnosticId = anchor.AnchorKind.Substring("Diagnostic:".Length);
                actionsAtAnchor =
                [
                    new RefactoringActionDescriptor(
                        $"diagnostic_{diagnosticId}",
                        $"Diagnostic: {diagnosticId}",
                        "analyzer",
                        "roslynator_diagnostic",
                        "info",
                        new PolicyDecisionInfo("allow", "diagnostic", "Found by analyzer"),
                        new SourceLocation(anchor.FilePath, anchor.Line, anchor.Column),
                        diagnosticId)
                ];
            }

            if (actionsAtAnchor.Length == 0)
                continue;

            foreach (var action in actionsAtAnchor)
            {
                var normalizedRiskLevel = NormalizeRiskLevel(action.RiskLevel, action.Origin, action.Category);
                var normalizedCategory = NormalizeCategory(action.Category, action.Origin, action.Title);
                var match = new CodeSmellMatch(
                    action.Title,
                    normalizedCategory,
                    new SourceLocation(anchor.FilePath, anchor.Line, anchor.Column),
                    action.Origin,
                    normalizedRiskLevel,
                    DetermineReviewKind(normalizedCategory, normalizedRiskLevel, action.Origin, action.Title));

                if (!filters.Accepts(match))
                    continue;

                actions.Add(match);
            }
        }

        var deduped = DeduplicateMatches(actions, warnings);
        var prioritized = PrioritizeMatches(deduped, filters.ReviewMode);
        var limitedMatches = filters.ApplyLimit(prioritized);

        if (limitedMatches.Count == 0 && (filters.RiskLevels is not null || filters.Categories is not null))
            warnings.Add("No findings matched the requested riskLevels/categories filters.");

        if (string.Equals(filters.ReviewMode, CodeSmellReviewModes.Conservative, StringComparison.Ordinal))
            warnings.Add("reviewMode=conservative suppresses lightweight style and trivia findings when stronger review concerns are available.");

        var aggregatedResult = AggregateMatches(limitedMatches);

        return new FindCodeSmellsResult(
            aggregatedResult.Summary,
            aggregatedResult.Findings,
            warnings.Count == 0 ? null : warnings,
            CreateContext(document.FilePath, warnings));
    }

    private async Task<IReadOnlyList<AnchorPosition>> CollectAnchorsAsync(Document document, List<string> warnings, CancellationToken ct)
    {
        var declarationAnchors = new List<AnchorPosition>();

        if (document.SupportsSyntaxTree)
        {
            var syntaxRoot = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (syntaxRoot is null)
                warnings.Add($"Skipped document without syntax root: {document.FilePath ?? document.Name}");
            else
                declarationAnchors.AddRange(EnumerateDeclarationAnchors(syntaxRoot, document.FilePath));
        }

        var diagnosticAnchors = await EnumerateDiagnosticAnchorsAsync(document, ct).ConfigureAwait(false);

        var ordered = diagnosticAnchors
            .OrderBy(static anchor => anchor.FilePath, StringComparer.Ordinal)
            .ThenBy(static anchor => anchor.Line)
            .ThenBy(static anchor => anchor.Column)
            .ThenBy(static anchor => anchor.AnchorKind, StringComparer.Ordinal)
            .Concat(
                declarationAnchors
                    .OrderBy(static anchor => anchor.FilePath, StringComparer.Ordinal)
                    .ThenBy(static anchor => anchor.Line)
                    .ThenBy(static anchor => anchor.Column)
                    .ThenBy(static anchor => anchor.AnchorKind, StringComparer.Ordinal))
            .ToArray();

        var deduplicated = new List<AnchorPosition>(ordered.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in ordered)
        {
            var key = CreateAnchorDeduplicationKey(anchor);
            if (!seen.Add(key))
                continue;

            deduplicated.Add(anchor);
        }

        if (deduplicated.Count <= MaximumScannedAnchors)
            return deduplicated;

        warnings.Add($"Anchor discovery truncated to {MaximumScannedAnchors} positions from {deduplicated.Count}.");
        return deduplicated.Take(MaximumScannedAnchors).ToArray();
    }

    private static IEnumerable<AnchorPosition> EnumerateDeclarationAnchors(SyntaxNode syntaxRoot, string? filePath)
    {
        var normalizedPath = filePath ?? string.Empty;

        foreach (var node in syntaxRoot.DescendantNodes())
        {
            if (!IsDeclarationAnchorCandidate(node))
                continue;

            var lineSpan = node.GetLocation().GetLineSpan();
            var start = lineSpan.StartLinePosition;
            var anchorPath = string.IsNullOrWhiteSpace(lineSpan.Path) ? normalizedPath : lineSpan.Path;
            if (string.IsNullOrWhiteSpace(anchorPath))
                continue;

            yield return new AnchorPosition(
                anchorPath,
                start.Line + 1,
                start.Character + 1,
                node.GetType().Name,
                IsDiagnostic: false);
        }
    }

    private async Task<IReadOnlyList<AnchorPosition>> EnumerateDiagnosticAnchorsAsync(Document document, CancellationToken ct)
    {
        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null)
            return [];

        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (tree is null)
            return [];

        var anchors = new List<AnchorPosition>();

        // Get compiler diagnostics
        var compilerAnchors = GetCompilerDiagnostics(compilation, tree);
        anchors.AddRange(compilerAnchors);

        // Get Roslynator analyzer diagnostics
        var analyzerAnchors = await GetRoslynatorDiagnosticsAsync(compilation, tree, ct).ConfigureAwait(false);
        anchors.AddRange(analyzerAnchors);

        return anchors;
    }

    private static IEnumerable<AnchorPosition> GetCompilerDiagnostics(Compilation compilation, SyntaxTree tree)
    {
        var anchors = new List<AnchorPosition>();

        foreach (var diagnostic in compilation.GetDiagnostics())
        {
            if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree is null)
                continue;

            if (!ReferenceEquals(diagnostic.Location.SourceTree, tree))
                continue;

            anchors.Add(CreateDiagnosticAnchor(diagnostic));
        }

        return anchors;
    }

    private async Task<IReadOnlyList<AnchorPosition>> GetRoslynatorDiagnosticsAsync(Compilation compilation, SyntaxTree tree, CancellationToken ct)
    {
        var anchors = new List<AnchorPosition>();

        var (analyzers, analyzerError) = _analyzerCatalog.GetCatalog();
        if (analyzerError is not null || analyzers.IsDefaultOrEmpty)
            return anchors;

        try
        {
            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
            var allDiagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync(ct).ConfigureAwait(false);
            var analyzerDiagnostics = allDiagnostics.Where(d => d.Location.IsInSource && d.Location.SourceTree == tree);

            foreach (var diagnostic in analyzerDiagnostics)
                anchors.Add(CreateDiagnosticAnchor(diagnostic));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _ = exception;
        }

        return anchors;
    }

    private static AnchorPosition CreateDiagnosticAnchor(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        var start = lineSpan.StartLinePosition;
        var anchorPath = string.IsNullOrWhiteSpace(lineSpan.Path)
            ? diagnostic.Location.SourceTree?.FilePath
            : lineSpan.Path;

        return new AnchorPosition(
            anchorPath ?? string.Empty,
            start.Line + 1,
            start.Character + 1,
            $"Diagnostic:{diagnostic.Id}",
            IsDiagnostic: true);
    }

    private static FindCodeSmellsResult CreateInvalidInputResult(string message, params (string Key, string? Value)[] details)
        => new(EmptySummary, Array.Empty<CodeSmellFindingEntry>(), null, UnknownContext, AgentErrorInfo.Create(ErrorCodes.InvalidInput, message, "Adjust input and retry find_codesmells.", details));

    private static (FindCodeSmellFilters? Filters, FindCodeSmellsResult? Error) ValidateRequest(FindCodeSmellsRequest request)
    {
        var path = NormalizePath(request.Path);
        if (path is null)
            return defaultError(CreateInvalidInputResult("path is required.", ("field", "path")));

        if (request.MaxFindings is <= 0)
        {
            return defaultError(CreateInvalidInputResult(
                "maxFindings must be greater than 0 when provided.",
                ("field", "maxFindings"),
                ("provided", request.MaxFindings.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        var (riskLevels, riskError) = NormalizeRiskLevels(request.RiskLevels);
        if (riskError is not null)
            return defaultError(riskError);

        var (categories, categoryError) = NormalizeCategories(request.Categories);
        if (categoryError is not null)
        {
            return defaultError(categoryError);
        }

        var (reviewMode, reviewModeError) = NormalizeReviewMode(request.ReviewMode);
        if (reviewModeError is not null)
            return defaultError(reviewModeError);

        return (new FindCodeSmellFilters(path, request.MaxFindings, riskLevels, categories, reviewMode), null);

        static (FindCodeSmellFilters? Filters, FindCodeSmellsResult Error) defaultError(FindCodeSmellsResult error)
            => (default, error);
    }

    private static string? NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private static (HashSet<string>? RiskLevels, FindCodeSmellsResult? Error) NormalizeRiskLevels(IReadOnlyList<string>? riskLevels)
    {
        if (riskLevels is null || riskLevels.Count == 0)
            return (null, null);

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var riskLevel in riskLevels)
        {
            var normalizedRiskLevel = NormalizePath(riskLevel);
            if (normalizedRiskLevel is null)
            {
                return (null, CreateInvalidInputResult(
                    "riskLevels must contain non-empty values.",
                    ("field", "riskLevels")));
            }

            var canonicalRiskLevel = NormalizeRiskLevelFilter(normalizedRiskLevel);
            if (canonicalRiskLevel is null)
            {
                return (null, CreateInvalidInputResult(
                    $"riskLevels must be drawn from: {string.Join(", ", SupportedRiskLevels)}.",
                    ("field", "riskLevels"),
                    ("provided", normalizedRiskLevel)));
            }

            normalized.Add(canonicalRiskLevel);
        }

        return (normalized, null);
    }

    private static (HashSet<string>? Categories, FindCodeSmellsResult? Error) NormalizeCategories(IReadOnlyList<string>? categories)
    {
        if (categories is null || categories.Count == 0)
        {
            return (null, null);
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories)
        {
            var normalizedCategory = NormalizePath(category);
            if (normalizedCategory is null)
            {
                return (null, CreateInvalidInputResult(
                    "categories must contain non-empty values.",
                    ("field", "categories")));
            }

            var canonicalCategory = NormalizeCategoryFilter(normalizedCategory);
            if (canonicalCategory is null)
            {
                return (null, CreateInvalidInputResult(
                    $"categories must be drawn from: {string.Join(", ", SupportedCategories)}.",
                    ("field", "categories"),
                    ("provided", normalizedCategory)));
            }

            normalized.Add(canonicalCategory);
        }

        return (normalized, null);
    }

    private static (string ReviewMode, FindCodeSmellsResult? Error) NormalizeReviewMode(string? reviewMode)
    {
        var normalized = NormalizePath(reviewMode);
        if (normalized is null)
        {
            return (CodeSmellReviewModes.Default, null);
        }

        var canonical = normalized.ToLowerInvariant();
        if (SupportedReviewModes.Contains(canonical, StringComparer.Ordinal))
        {
            return (canonical, null);
        }

        return (CodeSmellReviewModes.Default, CreateInvalidInputResult(
            $"reviewMode must be drawn from: {string.Join(", ", SupportedReviewModes)}.",
            ("field", "reviewMode"),
            ("provided", normalized)));
    }

    private static string CreateAnchorDeduplicationKey(AnchorPosition anchor)
    {
        var path = NormalizePathForDeduplication(anchor.FilePath);
        return anchor.IsDiagnostic
            ? string.Join('|', path, anchor.Line, anchor.Column, anchor.AnchorKind)
            : string.Join('|', path, anchor.Line);
    }

    private static string NormalizePathForDeduplication(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    private static RiskLevel MapRisk(string riskLevel)
    {
        return NormalizeRiskLevel(riskLevel, origin: null, category: null) switch
        {
            "low" => RiskLevel.Low,
            "review_required" or "medium" => RiskLevel.Medium,
            _ => RiskLevel.High
        };
    }

    private static string NormalizeRiskLevel(string? riskLevel, string? origin, string? category)
    {
        if (string.Equals(origin, "roslynator_diagnostic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "analyzer", StringComparison.OrdinalIgnoreCase))
        {
            return "info";
        }

        return (riskLevel ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => "info",
            "safe" or "low" => "low",
            "review_required" or "medium" => "review_required",
            "blocked" or "high" => "high",
            "info" => "info",
            var value => value
        };
    }

    private static string NormalizeCategory(string? category, string? origin, string? title)
    {
        var normalized = (category ?? string.Empty).Trim().ToLowerInvariant();
        if (string.Equals(origin, "roslynator_diagnostic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "analyzer", StringComparison.OrdinalIgnoreCase))
        {
            return "analyzer";
        }

        if (normalized.Contains("performance", StringComparison.Ordinal))
        {
            return "performance";
        }

        if (normalized.Contains("style", StringComparison.Ordinal)
            || string.Equals(title, "Add braces", StringComparison.OrdinalIgnoreCase)
            || string.Equals(title, "Remove braces", StringComparison.OrdinalIgnoreCase))
        {
            return "style";
        }

        if (normalized.Contains("design", StringComparison.Ordinal)
            || normalized.Contains("architecture", StringComparison.Ordinal))
        {
            return "design";
        }

        if (normalized.Contains("correct", StringComparison.Ordinal)
            || normalized.Contains("bug", StringComparison.Ordinal))
        {
            return "correctness";
        }

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized switch
            {
                "refactoring" or "readability" or "maintainability" => "maintainability",
                _ => "maintainability"
            };
        }

        return "maintainability";
    }

    private static string? NormalizeRiskLevelFilter(string riskLevel)
        => riskLevel.Trim().ToLowerInvariant() switch
        {
            "safe" or "low" => "low",
            "medium" or "review_required" => "review_required",
            "blocked" or "high" => "high",
            "info" => "info",
            _ => null
        };

    private static string? NormalizeCategoryFilter(string category)
        => category.Trim().ToLowerInvariant() switch
        {
            "analyzer" => "analyzer",
            "bug" or "correctness" => "correctness",
            "design" or "architecture" => "design",
            "maintainability" or "readability" or "refactoring" => "maintainability",
            "performance" => "performance",
            "style" => "style",
            _ => null
        };

    private static ResultContextMetadata CreateContext(string? documentPath, IReadOnlyList<string> warnings)
    {
        var degradedReasons = new List<string>();
        if (warnings.Any(static warning => warning.Contains("Skipped", StringComparison.Ordinal)))
        {
            degradedReasons.Add("analysis_positions_skipped");
        }

        if (warnings.Any(static warning => warning.Contains("truncated", StringComparison.OrdinalIgnoreCase)))
        {
            degradedReasons.Add("anchor_scan_truncated");
        }

        var limitations = new List<string>();
        if (SourceVisibility.IsGeneratedLike(documentPath))
        {
            limitations.Add("Generated or intermediate source can skew results toward analyzer-driven findings.");
        }

        return new ResultContextMetadata(
            SourceVisibility.DetermineResultSourceBias([documentPath]),
            degradedReasons.Count > 0 ? ResultCompletenessStates.Partial : ResultCompletenessStates.Complete,
            limitations,
            degradedReasons,
            degradedReasons.Count > 0
                ? "If findings look incomplete, restore/build the workspace and retry find_codesmells."
                : null);
    }

    private static IReadOnlyList<CodeSmellMatch> DeduplicateMatches(IReadOnlyList<CodeSmellMatch> matches, List<string> warnings)
    {
        if (matches.Count == 0)
        {
            return matches;
        }

        var deduped = new List<CodeSmellMatch>(matches.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateCount = 0;
        var collapsedNearbyCount = 0;

        foreach (var match in matches
                     .OrderBy(static item => item.Location.FilePath, StringComparer.Ordinal)
                     .ThenBy(static item => item.Location.Line)
                     .ThenBy(static item => item.Location.Column)
                     .ThenBy(static item => item.Title, StringComparer.Ordinal)
                     .ThenBy(static item => item.Category, StringComparer.Ordinal)
                     .ThenBy(static item => item.Origin, StringComparer.Ordinal))
        {
            var key = string.Join('|',
                NormalizePathForDeduplication(match.Location.FilePath),
                match.Location.Line,
                match.Title,
                match.Category,
                match.Origin,
                match.RiskLevel);

            if (!seen.Add(key))
            {
                duplicateCount++;
                continue;
            }

            if (ShouldCollapseNearbyStylisticSuggestion(deduped, match))
            {
                collapsedNearbyCount++;
                continue;
            }

            deduped.Add(match);
        }

        if (duplicateCount > 0)
        {
            warnings.Add($"Deduplicated {duplicateCount} repetitive findings by title/category/line.");
        }

        if (collapsedNearbyCount > 0)
        {
            warnings.Add($"Collapsed {collapsedNearbyCount} nearby repetitive stylistic findings.");
        }

        return deduped;
    }

    private static IReadOnlyList<CodeSmellMatch> PrioritizeMatches(IReadOnlyList<CodeSmellMatch> matches, string reviewMode)
    {
        var ranked = matches
            .Where(match => ShouldIncludeInReviewMode(match, matches, reviewMode))
            .OrderBy(GetReviewKindPriority)
            .ThenByDescending(GetRiskPriority)
            .ThenBy(GetCategoryPriority)
            .ThenBy(static match => match.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(static match => match.Location.Line)
            .ThenBy(static match => match.Location.Column)
            .ThenBy(static match => match.Title, StringComparer.Ordinal)
            .ToArray();

        return ranked.Length > 0 ? ranked : matches
            .OrderBy(GetReviewKindPriority)
            .ThenByDescending(GetRiskPriority)
            .ThenBy(GetCategoryPriority)
            .ThenBy(static match => match.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(static match => match.Location.Line)
            .ThenBy(static match => match.Location.Column)
            .ThenBy(static match => match.Title, StringComparer.Ordinal)
            .ToArray();
    }

    private static AggregatedCodeSmellsResult AggregateMatches(IReadOnlyList<CodeSmellMatch> matches)
    {
        if (matches.Count == 0)
        {
            return new AggregatedCodeSmellsResult(EmptySummary, Array.Empty<CodeSmellFindingEntry>());
        }

        var findings = matches
            .GroupBy(CreateFindingIdentityKey, StringComparer.Ordinal)
            .Select(static group => CreateFindingEntry(group.ToArray()))
            .OrderBy(static finding => GetRiskSortOrder(finding.RiskLevel))
            .ThenBy(static finding => GetCategorySortOrder(finding.Category))
            .ThenBy(static finding => GetReviewKindPriority(finding.ReviewKind))
            .ThenByDescending(static finding => finding.OccurrenceCount)
            .ThenBy(static finding => finding.Title, StringComparer.Ordinal)
            .ToArray();

        var summary = new CodeSmellsSummary(
            findings.Length,
            findings.Sum(static finding => finding.OccurrenceCount));

        return new AggregatedCodeSmellsResult(summary, findings);
    }

    private static CodeSmellFindingEntry CreateFindingEntry(IReadOnlyList<CodeSmellMatch> matches)
    {
        var exemplar = matches[0];
        var occurrenceFiles = matches
            .Select(static match => match.Location)
            .OrderBy(static location => location.FilePath, StringComparer.Ordinal)
            .ThenBy(static location => location.Line)
            .ThenBy(static location => location.Column)
            .GroupBy(static location => location.FilePath, StringComparer.Ordinal)
            .Select(group => new CodeSmellOccurrenceFile(
                group.Key,
                group.Select(static location => new ReferencePosition(location.Line, location.Column)).ToArray()))
            .ToArray();

        return new CodeSmellFindingEntry(
            CreateFindingFingerprint(exemplar),
            exemplar.Title,
            exemplar.RiskLevel,
            exemplar.Category,
            exemplar.ReviewKind,
            matches.Count,
            occurrenceFiles);
    }

    private static string CreateFindingIdentityKey(CodeSmellMatch match)
        => string.Join('|',
            match.Title,
            match.Origin,
            match.RiskLevel,
            match.Category,
            match.ReviewKind);

    private static string CreateFindingFingerprint(CodeSmellMatch match)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(CreateFindingIdentityKey(match)));
        return $"finding:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string DetermineReviewKind(string category, string riskLevel, string? origin, string title)
    {
        if (category == "style")
        {
            return CodeSmellReviewKinds.StyleSuggestion;
        }

        if (category == "analyzer" || string.Equals(origin, "roslynator_diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            return CodeSmellReviewKinds.CodeFixHint;
        }

        return riskLevel is "high" or "review_required"
            ? CodeSmellReviewKinds.ReviewConcern
            : CodeSmellReviewKinds.CodeFixHint;
    }

    private static bool ShouldIncludeInReviewMode(CodeSmellMatch candidate, IReadOnlyList<CodeSmellMatch> allMatches, string reviewMode)
    {
        if (!string.Equals(reviewMode, CodeSmellReviewModes.Conservative, StringComparison.Ordinal))
        {
            return true;
        }

        if (candidate.ReviewKind == CodeSmellReviewKinds.ReviewConcern)
        {
            return true;
        }

        if (candidate.RiskLevel == "high" || candidate.RiskLevel == "review_required")
        {
            return true;
        }

        var hasStrongerSignals = allMatches.Any(static match =>
            match.ReviewKind == CodeSmellReviewKinds.ReviewConcern
            || match.RiskLevel is "high" or "review_required");

        if (!hasStrongerSignals)
        {
            return candidate.ReviewKind != CodeSmellReviewKinds.StyleSuggestion;
        }

        return candidate.ReviewKind == CodeSmellReviewKinds.CodeFixHint
               && candidate.Category != "style"
               && candidate.Origin != "roslynator_diagnostic"
               && candidate.RiskLevel != "info";
    }

    private static int GetReviewKindPriority(CodeSmellMatch match)
        => GetReviewKindPriority(match.ReviewKind);

    private static int GetReviewKindPriority(string? reviewKind)
        => reviewKind switch
        {
            CodeSmellReviewKinds.ReviewConcern => 0,
            CodeSmellReviewKinds.CodeFixHint => 1,
            CodeSmellReviewKinds.StyleSuggestion => 2,
            _ => 3
        };

    private static int GetRiskPriority(CodeSmellMatch match)
        => GetRiskPriority(match.RiskLevel);

    private static int GetRiskPriority(string? riskLevel)
        => riskLevel switch
        {
            "high" => 3,
            "review_required" => 2,
            "low" => 1,
            _ => 0
        };

    private static int GetRiskSortOrder(string? riskLevel)
        => riskLevel switch
        {
            "high" => 0,
            "review_required" => 1,
            "low" => 2,
            "info" => 3,
            _ => 4
        };

    private static int GetCategoryPriority(CodeSmellMatch match)
        => GetCategoryPriority(match.Category);

    private static int GetCategoryPriority(string? category)
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

    private static int GetCategorySortOrder(string? category)
        => GetCategoryPriority(category);

    private static bool IsDeclarationAnchorCandidate(SyntaxNode node)
        => node is BaseTypeDeclarationSyntax
            or DelegateDeclarationSyntax
            or BaseMethodDeclarationSyntax
            or PropertyDeclarationSyntax
            or EventDeclarationSyntax
            or EventFieldDeclarationSyntax
            or FieldDeclarationSyntax
            or LocalFunctionStatementSyntax
            or IfStatementSyntax
            or ElseClauseSyntax
            or ForStatementSyntax
            or ForEachStatementSyntax
            or ForEachVariableStatementSyntax
            or WhileStatementSyntax
            or DoStatementSyntax
            or UsingStatementSyntax
            or LockStatementSyntax
            or SwitchStatementSyntax
            or TryStatementSyntax
            or CatchClauseSyntax;

    private static bool ShouldCollapseNearbyStylisticSuggestion(IReadOnlyList<CodeSmellMatch> accepted, CodeSmellMatch candidate)
    {
        if (!IsNearbyStylisticSuggestion(candidate))
        {
            return false;
        }

        var previous = accepted.LastOrDefault();
        return previous is not null
               && IsNearbyStylisticSuggestion(previous)
               && previous.Location.FilePath.MatchesByNormalizedPath(candidate.Location.FilePath)
               && string.Equals(previous.Title, candidate.Title, StringComparison.OrdinalIgnoreCase)
               && string.Equals(previous.Category, candidate.Category, StringComparison.OrdinalIgnoreCase)
               && string.Equals(previous.Origin, candidate.Origin, StringComparison.OrdinalIgnoreCase)
               && string.Equals(previous.RiskLevel, candidate.RiskLevel, StringComparison.OrdinalIgnoreCase)
               && Math.Abs(previous.Location.Line - candidate.Location.Line) <= 1;
    }

    private static bool IsNearbyStylisticSuggestion(CodeSmellMatch match)
        => string.Equals(match.Title, "Add braces", StringComparison.OrdinalIgnoreCase)
           || string.Equals(match.Title, "Remove braces", StringComparison.OrdinalIgnoreCase);

    private sealed record AggregatedCodeSmellsResult(
        CodeSmellsSummary Summary,
        IReadOnlyList<CodeSmellFindingEntry> Findings);

    private sealed record AnchorPosition(string FilePath, int Line, int Column, string AnchorKind, bool IsDiagnostic);

    private sealed record CodeSmellMatch(
        string Title,
        string Category,
        SourceLocation Location,
        string Origin,
        string RiskLevel,
        string ReviewKind);

    private sealed record FindCodeSmellFilters(
        string Path,
        int? MaxFindings,
        HashSet<string>? RiskLevels,
        HashSet<string>? Categories,
        string ReviewMode)
    {
        public bool Accepts(CodeSmellMatch match)
        {
            if (RiskLevels is not null && !RiskLevels.Contains(match.RiskLevel))
            {
                return false;
            }

            if (Categories is not null && !Categories.Contains(match.Category))
            {
                return false;
            }

            return true;
        }

        public bool HasReachedLimit(int acceptedCount)
            => MaxFindings is not null && acceptedCount >= MaxFindings.Value;

        public IReadOnlyList<CodeSmellMatch> ApplyLimit(IReadOnlyList<CodeSmellMatch> matches)
            => MaxFindings is null ? matches : matches.Take(MaxFindings.Value).ToArray();
    }
}
