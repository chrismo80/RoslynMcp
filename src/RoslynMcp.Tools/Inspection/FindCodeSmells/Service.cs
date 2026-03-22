using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Tools.Infrastructure;
using RoslynMcp.Tools.Infrastructure.Services;

namespace RoslynMcp.Tools.Inspection.FindCodeSmells;

public sealed class Service(RoslynMcp.Tools.Infrastructure.Services.Workspace workspace)
{
    private static readonly string[] SupportedRiskLevels = ["low", "review_required", "high", "info"];
    private static readonly string[] SupportedCategories = ["analyzer", "correctness", "design", "maintainability", "performance", "style"];
    private static readonly string[] SupportedReviewModes = [ReviewModes.Default, ReviewModes.Conservative];
    private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers = LoadAnalyzers();
    private static readonly CodeSmellsSummary EmptySummary = new(0, 0);
    private static readonly Context UnknownContext = new(SourceBiases.Unknown, CompletenessStates.Degraded, [], []);

    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var validation = Validate(request);
        if (validation.Error is not null)
            return validation.Error;

        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Failure("no_solution_loaded", "No solution is currently loaded.", "Call load_solution first to select a solution before finding code smells.");
        }

        var document = session.Solution.Projects
            .SelectMany(static project => project.Documents)
            .Where(candidate => candidate.FilePath.MatchesByNormalizedPath(validation.Filters!.Path))
            .OrderBy(static candidate => candidate.FilePath, StringComparer.Ordinal)
            .ToArray();

        if (document.Length == 0)
            return Failure("invalid_path", "path did not match any loaded document.", "Use a source document path that exists in the loaded solution.", ("field", "path"), ("provided", validation.Filters!.Path));

        if (document.Length > 1)
            return Failure("invalid_path", "path matched multiple loaded documents.", "Provide a unique source document path from the loaded solution.", ("field", "path"), ("provided", validation.Filters!.Path));

        var warnings = new List<string>();
        var filters = validation.Filters!;
        var matches = await CollectMatchesAsync(document[0], warnings, cancellationToken).ConfigureAwait(false);
        var deduped = Deduplicate(matches, warnings);
        var filtered = deduped.Where(filters.Accepts).ToArray();

        if (filtered.Length == 0 && (filters.RiskLevels is not null || filters.Categories is not null))
            warnings.Add("No findings matched the requested riskLevels/categories filters.");

        var prioritized = Prioritize(filtered, filters.ReviewMode);
        if (string.Equals(filters.ReviewMode, ReviewModes.Conservative, StringComparison.Ordinal))
            warnings.Add("reviewMode=conservative suppresses lightweight style and trivia findings when stronger review concerns are available.");

        if (filters.MaxFindings is not null)
            prioritized = [.. prioritized.Take(filters.MaxFindings.Value)];

        return new Result(
            new CodeSmellsSummary(prioritized.GroupBy(CreateIdentity).Count(), prioritized.Length),
            Aggregate(prioritized),
            warnings.Count == 0 ? null : warnings,
            CreateContext(document[0].FilePath, warnings))
            .WithWorkspaceRelativePaths();
    }

    private static async Task<IReadOnlyList<Match>> CollectMatchesAsync(Document document, List<string> warnings, CancellationToken cancellationToken)
    {
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);

        if (syntaxRoot is null || semanticModel is null || compilation is null || syntaxTree is null || string.IsNullOrWhiteSpace(document.FilePath))
        {
            warnings.Add($"Skipped document without full Roslyn state: {document.FilePath ?? document.Name}");
            return [];
        }

        var matches = new List<Match>();
        matches.AddRange(GetDiagnosticMatches(compilation, syntaxTree));
        matches.AddRange(await GetAnalyzerMatchesAsync(compilation, syntaxTree, cancellationToken).ConfigureAwait(false));
        matches.AddRange(GetCustomMatches(syntaxRoot, semanticModel, document.FilePath));
        return matches;
    }

    private static IReadOnlyList<Match> GetDiagnosticMatches(Compilation compilation, SyntaxTree syntaxTree)
        => [.. compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Id == "CS0162")
            .Where(diagnostic => diagnostic.Location.IsInSource && ReferenceEquals(diagnostic.Location.SourceTree, syntaxTree))
            .Select(static diagnostic => CreateAnalyzerMatch($"Diagnostic: {diagnostic.Id}", diagnostic.Location))];

    private static async Task<IReadOnlyList<Match>> GetAnalyzerMatchesAsync(Compilation compilation, SyntaxTree syntaxTree, CancellationToken cancellationToken)
    {
        if (Analyzers.IsDefaultOrEmpty)
            return [];

        try
        {
            var compilationWithAnalyzers = compilation.WithAnalyzers(Analyzers);
            var diagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            return [.. diagnostics
                .Where(static diagnostic => diagnostic.Id.StartsWith("RCS", StringComparison.Ordinal))
                .Where(diagnostic => diagnostic.Location.IsInSource && ReferenceEquals(diagnostic.Location.SourceTree, syntaxTree))
                .Select(static diagnostic => CreateAnalyzerMatch($"Diagnostic: {diagnostic.Id}", diagnostic.Location))];
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<Match> GetCustomMatches(SyntaxNode syntaxRoot, SemanticModel semanticModel, string filePath)
    {
        foreach (var catchClause in syntaxRoot.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            if (catchClause.Block is { Statements.Count: 0 })
                yield return CreateMatch("Empty catch block", "correctness", "heuristic", "high", ReviewKinds.ReviewConcern, filePath, catchClause.CatchKeyword.GetLocation());
        }

        foreach (var method in syntaxRoot.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            if (method.ParameterList.Parameters.Count > 5)
                yield return CreateMatch("Too many parameters", "design", "heuristic", "review_required", ReviewKinds.ReviewConcern, filePath, method.ParameterList.GetLocation());
        }

        foreach (var method in syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!LooksLikePascalCase(method.Identifier.ValueText))
                yield return CreateMatch("Method name does not follow PascalCase", "style", "heuristic", "low", ReviewKinds.StyleSuggestion, filePath, method.Identifier.GetLocation());

            foreach (var parameter in method.ParameterList.Parameters)
            {
                if (!LooksLikeCamelCase(parameter.Identifier.ValueText))
                    yield return CreateMatch("Parameter name does not follow camelCase", "style", "heuristic", "low", ReviewKinds.StyleSuggestion, filePath, parameter.Identifier.GetLocation());
            }
        }

        foreach (var literal in syntaxRoot.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (literal.IsKind(SyntaxKind.NumericLiteralExpression) && IsMagicNumber(literal.Token.Value))
                yield return CreateMatch("Magic number", "maintainability", "heuristic", "low", ReviewKinds.CodeFixHint, filePath, literal.GetLocation());
        }

        foreach (var field in syntaxRoot.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables)
            {
                var symbol = semanticModel.GetDeclaredSymbol(variable);
                if (symbol is null || symbol.DeclaredAccessibility != Accessibility.Private)
                    continue;

                var references = symbol.DeclaringSyntaxReferences;
                if (references.Length == 0)
                    continue;

                var identifiers = syntaxRoot.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == symbol.Name).ToArray();
                if (identifiers.Length == 1)
                    yield return CreateMatch("Unused private field", "maintainability", "heuristic", "low", ReviewKinds.CodeFixHint, filePath, variable.Identifier.GetLocation());
            }
        }
    }

    private static Match CreateAnalyzerMatch(string title, Location location)
    {
        var lineSpan = location.GetLineSpan();
        var start = lineSpan.StartLinePosition;
        return new Match(title, "analyzer", new SourceLocation(lineSpan.Path ?? string.Empty, start.Line + 1, start.Character + 1), "roslynator_diagnostic", "info", ReviewKinds.CodeFixHint);
    }

    private static Match CreateMatch(string title, string category, string origin, string riskLevel, string reviewKind, string filePath, Location location)
    {
        var lineSpan = location.GetLineSpan();
        var start = lineSpan.StartLinePosition;
        var path = string.IsNullOrWhiteSpace(lineSpan.Path) ? filePath : lineSpan.Path;
        return new Match(title, category, new SourceLocation(path, start.Line + 1, start.Character + 1), origin, riskLevel, reviewKind);
    }

    private static IReadOnlyList<Match> Deduplicate(IReadOnlyList<Match> matches, List<string> warnings)
    {
        var deduped = new List<Match>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateCount = 0;

        foreach (var match in matches.OrderBy(static item => item.Location.FilePath, StringComparer.Ordinal).ThenBy(static item => item.Location.Line).ThenBy(static item => item.Location.Column).ThenBy(static item => item.Title, StringComparer.Ordinal))
        {
            var key = string.Join('|', NormalizePath(match.Location.FilePath), match.Location.Line, match.Title, match.Category, match.Origin, match.RiskLevel);
            if (!seen.Add(key))
            {
                duplicateCount++;
                continue;
            }

            deduped.Add(match);
        }

        if (duplicateCount > 0)
            warnings.Add($"Deduplicated {duplicateCount} repetitive findings by title/category/line.");

        return deduped;
    }

    private static Match[] Prioritize(IEnumerable<Match> matches, string reviewMode)
    {
        var all = matches.ToArray();
        var filtered = all.Where(match => ShouldInclude(match, all, reviewMode));
        return [.. filtered
            .OrderBy(GetReviewPriority)
            .ThenBy(static match => GetRiskSortOrder(match.RiskLevel))
            .ThenBy(static match => GetCategorySortOrder(match.Category))
            .ThenBy(static match => match.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(static match => match.Location.Line)
            .ThenBy(static match => match.Location.Column)
            .ThenBy(static match => match.Title, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<CodeSmellFindingEntry> Aggregate(IReadOnlyList<Match> matches)
        => [.. matches
            .GroupBy(CreateIdentity, StringComparer.Ordinal)
            .Select(static group => CreateFinding([.. group]))
            .OrderBy(static finding => GetRiskSortOrder(finding.RiskLevel))
            .ThenBy(static finding => GetCategorySortOrder(finding.Category))
            .ThenBy(static finding => GetReviewPriority(finding.ReviewKind))
            .ThenByDescending(static finding => finding.OccurrenceCount)
            .ThenBy(static finding => finding.Title, StringComparer.Ordinal)];

    private static CodeSmellFindingEntry CreateFinding(IReadOnlyList<Match> matches)
    {
        var exemplar = matches[0];
        var occurrenceFiles = matches
            .Select(static match => match.Location)
            .OrderBy(static location => location.FilePath, StringComparer.Ordinal)
            .ThenBy(static location => location.Line)
            .ThenBy(static location => location.Column)
            .GroupBy(static location => location.FilePath, StringComparer.Ordinal)
            .Select(static group => new CodeSmellOccurrenceFile(group.Key, [.. group.Select(static location => new ReferencePosition(location.Line, location.Column))]))
            .ToArray();

        return new CodeSmellFindingEntry(CreateFingerprint(exemplar), exemplar.Title, exemplar.RiskLevel, exemplar.Category, exemplar.ReviewKind, matches.Count, occurrenceFiles);
    }

    private static string CreateIdentity(Match match)
        => string.Join('|', match.Title, match.Origin, match.RiskLevel, match.Category, match.ReviewKind);

    private static string CreateFingerprint(Match match)
        => $"finding:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CreateIdentity(match)))).ToLowerInvariant()}";

    private static Context CreateContext(string? documentPath, IReadOnlyList<string> warnings)
    {
        var degradedReasons = new List<string>();
        if (warnings.Any(static warning => warning.Contains("Skipped", StringComparison.Ordinal)))
            degradedReasons.Add("analysis_positions_skipped");

        var limitations = new List<string>();
        if (SourceVisibility.IsGeneratedLike(documentPath))
            limitations.Add("Generated or intermediate source can skew results toward analyzer-driven findings.");

        return new Context(
            DetermineSourceBias(documentPath),
            degradedReasons.Count == 0 ? CompletenessStates.Complete : CompletenessStates.Partial,
            limitations,
            degradedReasons,
            degradedReasons.Count == 0 ? null : "If findings look incomplete, restore/build the workspace and retry find_codesmells.");
    }

    private static string DetermineSourceBias(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? SourceBiases.Unknown
            : SourceVisibility.IsGeneratedLike(path) ? SourceBiases.Generated : SourceBiases.Handwritten;

    private static (Filters? Filters, Result? Error) Validate(Request request)
    {
        var path = request.Path.NormalizeOptional();
        if (path is null)
            return (null, Failure("invalid_input", "path is required.", "Adjust input and retry find_codesmells.", ("field", "path")));

        if (request.MaxFindings is <= 0)
        {
            return (null, Failure("invalid_input", "maxFindings must be greater than 0 when provided.", "Adjust input and retry find_codesmells.",
                ("field", "maxFindings"), ("provided", request.MaxFindings.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        var (Values, Error) = NormalizeSet(request.RiskLevels, NormalizeRiskLevelFilter, "riskLevels");
        if (Error is not null)
            return (null, Error);

        var categories = NormalizeSet(request.Categories, NormalizeCategoryFilter, "categories");
        if (categories.Error is not null)
            return (null, categories.Error);

        var reviewMode = request.ReviewMode.NormalizeOptional()?.ToLowerInvariant() ?? ReviewModes.Default;
        if (!SupportedReviewModes.Contains(reviewMode, StringComparer.Ordinal))
        {
            return (null, Failure("invalid_input", $"reviewMode must be drawn from: {string.Join(", ", SupportedReviewModes)}.", "Adjust input and retry find_codesmells.",
                ("field", "reviewMode"), ("provided", request.ReviewMode)));
        }

        return (new Filters(path, request.MaxFindings, Values, categories.Values, reviewMode), null);
    }

    private static (HashSet<string>? Values, Result? Error) NormalizeSet(IReadOnlyList<string>? values, Func<string, string?> normalize, string field)
    {
        if (values is null || values.Count == 0)
            return (null, null);

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var item = value.NormalizeOptional();
            if (item is null)
                return (null, Failure("invalid_input", $"{field} must contain non-empty values.", "Adjust input and retry find_codesmells.", ("field", field)));

            var canonical = normalize(item);
            if (canonical is null)
            {
                var supported = field == "riskLevels" ? SupportedRiskLevels : SupportedCategories;
                return (null, Failure("invalid_input", $"{field} must be drawn from: {string.Join(", ", supported)}.", "Adjust input and retry find_codesmells.", ("field", field), ("provided", item)));
            }

            normalized.Add(canonical);
        }

        return (normalized, null);
    }

    private static Result Failure(string code, string message, string nextAction, params (string Key, string? Value)[] details)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nextAction"] = nextAction
        };

        foreach (var (key, value) in details)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                map[key] = value;
        }

        return new Result(EmptySummary, [], null, UnknownContext, new ErrorInfo(code, message, map)).WithWorkspaceRelativePaths();
    }

    private static string? NormalizeRiskLevelFilter(string value)
        => value.ToLowerInvariant() switch
        {
            "safe" or "low" => "low",
            "medium" or "review_required" => "review_required",
            "blocked" or "high" => "high",
            "info" => "info",
            _ => null
        };

    private static string? NormalizeCategoryFilter(string value)
        => value.ToLowerInvariant() switch
        {
            "analyzer" => "analyzer",
            "bug" or "correctness" => "correctness",
            "design" or "architecture" => "design",
            "maintainability" or "readability" or "refactoring" => "maintainability",
            "performance" => "performance",
            "style" => "style",
            _ => null
        };

    private static bool ShouldInclude(Match candidate, IReadOnlyList<Match> allMatches, string reviewMode)
    {
        if (!string.Equals(reviewMode, ReviewModes.Conservative, StringComparison.Ordinal))
            return true;

        if (candidate.ReviewKind == ReviewKinds.ReviewConcern || candidate.RiskLevel is "high" or "review_required")
            return true;

        var hasStrongerSignals = allMatches.Any(static match => match.ReviewKind == ReviewKinds.ReviewConcern || match.RiskLevel is "high" or "review_required");
        if (!hasStrongerSignals)
            return candidate.ReviewKind != ReviewKinds.StyleSuggestion;

        return candidate.ReviewKind == ReviewKinds.CodeFixHint && candidate.Category != "style" && candidate.Origin != "roslynator_diagnostic" && candidate.RiskLevel != "info";
    }

    private static int GetReviewPriority(Match match) => GetReviewPriority(match.ReviewKind);
    private static int GetReviewPriority(string? reviewKind) => reviewKind switch { ReviewKinds.ReviewConcern => 0, ReviewKinds.CodeFixHint => 1, ReviewKinds.StyleSuggestion => 2, _ => 3 };
    private static int GetCategorySortOrder(string? category) => category switch { "correctness" => 0, "design" => 1, "maintainability" => 2, "performance" => 3, "analyzer" => 4, "style" => 5, _ => 6 };
    private static int GetRiskSortOrder(string? riskLevel) => riskLevel switch { "high" => 0, "review_required" => 1, "low" => 2, "info" => 3, _ => 4 };

    private static bool LooksLikePascalCase(string name) => !string.IsNullOrWhiteSpace(name) && char.IsUpper(name[0]) && !name.Contains('_');
    private static bool LooksLikeCamelCase(string name) => !string.IsNullOrWhiteSpace(name) && char.IsLower(name[0]) && !name.Contains('_');

    private static bool IsMagicNumber(object? value)
        => value switch
        {
            int intValue => intValue is not (-1 or 0 or 1),
            long longValue => longValue is not (-1 or 0 or 1),
            float floatValue => floatValue is not (-1 or 0 or 1),
            double doubleValue => doubleValue is not (-1 or 0 or 1),
            decimal decimalValue => decimalValue is not (-1 or 0 or 1),
            _ => false
        };

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
    {
        try
        {
            var path = ResolveAnalyzerAssemblyPath();
            var loader = new AnalyzerAssemblyLoader();
            loader.AddDependencyLocation(path);
            var reference = new AnalyzerFileReference(path, loader);
            return reference.GetAnalyzers(LanguageNames.CSharp);
        }
        catch
        {
            return [];
        }
    }

    private static string ResolveAnalyzerAssemblyPath()
    {
        const string packageId = "roslynator.analyzers";
        const string packageVersion = "4.15.0";
        const string fileName = "Roslynator.CSharp.Analyzers.dll";
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        return Path.Combine(packagesRoot, packageId, packageVersion, "analyzers", "dotnet", "roslyn4.7", "cs", fileName);
    }

    private sealed class AnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, Assembly> _loadedAssemblies = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public AnalyzerAssemblyLoader() => AssemblyLoadContext.Default.Resolving += OnResolving;

        public void AddDependencyLocation(string fullPath)
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                lock (_gate)
                    _directories.Add(directory);
            }

            LoadFromPath(fullPath);
        }

        public Assembly LoadFromPath(string fullPath)
        {
            lock (_gate)
            {
                if (_loadedAssemblies.TryGetValue(fullPath, out var assembly))
                    return assembly;

                var loaded = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                _loadedAssemblies[fullPath] = loaded;
                return loaded;
            }
        }

        private Assembly? OnResolving(AssemblyLoadContext _, AssemblyName assemblyName)
        {
            lock (_gate)
            {
                foreach (var directory in _directories)
                {
                    var candidate = Path.Combine(directory, assemblyName.Name + ".dll");
                    if (_loadedAssemblies.TryGetValue(candidate, out var existing))
                        return existing;

                    if (File.Exists(candidate))
                        return LoadFromPath(candidate);
                }
            }

            return null;
        }
    }
}
