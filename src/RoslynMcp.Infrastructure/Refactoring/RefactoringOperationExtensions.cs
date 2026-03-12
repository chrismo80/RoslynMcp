using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Agent;
using System.Collections.Immutable;
using System.Text;

namespace RoslynMcp.Infrastructure.Refactoring;

internal static class RefactoringOperationExtensions
{
    public static SourceLocation ToSourceLocation(this Location location)
    {
        var span = location.GetLineSpan();
        var filePath = span.Path ?? string.Empty;
        var start = span.StartLinePosition;
        return new SourceLocation(filePath, start.Line + 1, start.Character + 1);
    }

    public static string GetLocationKey(this SourceLocation location)
        => string.Join(':', location.FilePath, location.Line, location.Column);

    public static async Task<SourceLocation> ToSourceLocationAsync(this Document document, TextSpan span, CancellationToken ct)
    {
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var line = text.Lines.GetLineFromPosition(span.Start);
        return new SourceLocation(document.FilePath ?? document.Name, line.LineNumber + 1, span.Start - line.Start + 1);
    }

    extension(Diagnostic diagnostic)
    {
        public bool IsSupportedDiagnostic()
            => RefactoringOperationOrchestrator.SupportedFixDiagnosticIds.Contains(diagnostic.Id);

        public string GetCodeFixCategory()
            => string.IsNullOrWhiteSpace(diagnostic.Descriptor.Category)
                ? RefactoringOperationOrchestrator.SupportedFixCategory
                : diagnostic.Descriptor.Category.Trim().ToLowerInvariant();
    }

    public static HashSet<string>? ToDiagnosticFilter(this IReadOnlyList<string>? diagnosticIds)
    {
        if (diagnosticIds == null || diagnosticIds.Count == 0)
        {
            return null;
        }

        var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in diagnosticIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                filter.Add(id.Trim());
            }
        }

        return filter.Count == 0 ? null : filter;
    }

    public static async Task<LocalDeclarationStatementSyntax?> TryGetUnusedLocalDeclarationAsync(
        this Document document,
        Diagnostic diagnostic,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root == null)
            return null;

        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        var declaration = token.Parent?.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (declaration == null)
            return null;

        if (declaration.Declaration.Variables.Count != 1)
            return null;

        return declaration;
    }

    public static CodeFixDescriptor ToFixDescriptor(
        this LocalDeclarationStatementSyntax declaration,
        Document document,
        Diagnostic diagnostic,
        int workspaceVersion)
    {
        var location = diagnostic.Location.ToSourceLocation();
        var variableName = declaration.Declaration.Variables[0].Identifier.ValueText;
        var title = $"Remove unused local variable '{variableName}'";
        var filePath = document.FilePath ?? document.Name;
        var fixId = BuildFixId(workspaceVersion, diagnostic.Id, declaration.Span.Start, declaration.Span.Length, filePath);
        return new CodeFixDescriptor(fixId, title, diagnostic.Id, RefactoringOperationOrchestrator.SupportedFixCategory, location, filePath);
    }

    public static CompilationUnitSyntax OrganizeUsings(this CompilationUnitSyntax root)
    {
        var updated = root.WithUsings(root.Usings.SortUsingDirectives());
        foreach (var namespaceDeclaration in updated.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            var orderedUsings = namespaceDeclaration.Usings.SortUsingDirectives();
            if (orderedUsings == namespaceDeclaration.Usings)
            {
                continue;
            }

            updated = updated.ReplaceNode(namespaceDeclaration, namespaceDeclaration.WithUsings(orderedUsings));
        }

        return updated;
    }

    public static SyntaxList<UsingDirectiveSyntax> SortUsingDirectives(this SyntaxList<UsingDirectiveSyntax> usings)
    {
        if (usings.Count <= 1)
            return usings;

        return SyntaxFactory.List(
            usings
                .OrderBy(static directive => directive.Alias == null ? 1 : 0)
                .ThenBy(static directive => directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? 1 : 0)
                .ThenBy(static directive => directive.Name?.ToString(), StringComparer.Ordinal)
                .ThenBy(static directive => directive.Alias?.Name.Identifier.ValueText ?? string.Empty, StringComparer.Ordinal));
    }

    public static bool IsValidIdentifier(this string candidate, ISymbol symbol)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        if (symbol.Language == LanguageNames.CSharp)
            return SyntaxFacts.IsValidIdentifier(candidate);

        return true;
    }

    extension(ISymbol symbol)
    {
        public ISet<string> GetSourceLocationKeys()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var location in symbol.Locations.Where(static l => l.IsInSource))
            {
                var sourceLocation = location.ToSourceLocation();
                keys.Add(sourceLocation.GetLocationKey());
            }

            return keys;
        }

        public async Task<IReadOnlyList<SourceLocation>> CollectAffectedLocationsAsync(Solution solution, CancellationToken ct)
        {
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var locations = new List<SourceLocation>();

            foreach (var reference in references)
            {
                ct.ThrowIfCancellationRequested();

                foreach (var location in reference.Locations.Where(location => location.Location.IsInSource))
                {
                    var sourceLocation = location.Location.ToSourceLocation();
                    var key = sourceLocation.GetLocationKey();
                    
                    if (seen.Add(key))
                        locations.Add(sourceLocation);
                }
            }

            foreach (var location in symbol.Locations.Where(static l => l.IsInSource))
            {
                var sourceLocation = location.ToSourceLocation();
                var key = sourceLocation.GetLocationKey();
                if (seen.Add(key))
                {
                    locations.Add(sourceLocation);
                }
            }

            return locations
                .OrderBy(loc => loc.FilePath, StringComparer.Ordinal)
                .ThenBy(loc => loc.Line)
                .ThenBy(loc => loc.Column)
                .ToList();
        }

        public bool WouldConflict(string newName)
        {
            if (string.Equals(symbol.Name, newName, StringComparison.Ordinal))
                return false;

            var members = symbol.ContainingType?.GetMembers(newName) ?? default;
            if (members.IsDefaultOrEmpty && symbol.ContainingNamespace != null)
            {
                members = symbol.ContainingNamespace.GetMembers(newName)
                    .Cast<ISymbol>()
                    .ToImmutableArray();
            }

            if (members.IsDefaultOrEmpty)
                return false;

            foreach (var member in members)
            {
                if (symbol.ConflictsWith(member))
                    return true;
            }

            return false;
        }

        public bool ConflictsWith(ISymbol existing)
        {
            var normalizedOriginal = symbol.OriginalDefinition ?? symbol;
            var normalizedExisting = existing.OriginalDefinition ?? existing;

            if (SymbolEqualityComparer.Default.Equals(normalizedOriginal, normalizedExisting))
            {
                return false;
            }

            if (normalizedOriginal.Kind != normalizedExisting.Kind)
            {
                return false;
            }

            if (normalizedOriginal is IMethodSymbol originalMethod && normalizedExisting is IMethodSymbol existingMethod)
            {
                if (originalMethod.Parameters.Length != existingMethod.Parameters.Length)
                    return false;

                for (var i = 0; i < originalMethod.Parameters.Length; i++)
                {
                    if (!SymbolEqualityComparer.Default.Equals(originalMethod.Parameters[i].Type, existingMethod.Parameters[i].Type))
                        return false;
                }

                return true;
            }

            if (normalizedOriginal is IPropertySymbol && normalizedExisting is IPropertySymbol)
                return true;

            if (normalizedOriginal is IFieldSymbol && normalizedExisting is IFieldSymbol)
                return true;

            if (normalizedOriginal is IEventSymbol && normalizedExisting is IEventSymbol)
                return true;

            if (normalizedOriginal is INamedTypeSymbol && normalizedExisting is INamedTypeSymbol)
                return true;

            return true;
        }
    }

    public static IEnumerable<Document> ResolveScopeDocuments(this Solution solution, string scope, string? path)
    {
        if (string.Equals(scope, "solution", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(path))
            return solution.Projects.SelectMany(static project => project.Documents);

        if (string.Equals(scope, "project", StringComparison.Ordinal))
        {
            return solution.Projects
                .Where(project => project.FilePath.MatchesByNormalizedPath(path)
                                  || string.Equals(project.Name, path, StringComparison.OrdinalIgnoreCase))
                .SelectMany(static project => project.Documents);
        }

        return solution.Projects
            .SelectMany(static project => project.Documents)
            .Where(document => document.FilePath.MatchesByNormalizedPath(path));
    }

    public static bool MatchesByNormalizedPath(this string? candidatePath, string path)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var normalizedCandidate = System.IO.Path.GetFullPath(candidatePath);
            var normalizedPath = System.IO.Path.GetFullPath(path);
            return string.Equals(normalizedCandidate, normalizedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(candidatePath, path, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsValidScope(this string scope)
        => string.Equals(scope, "document", StringComparison.Ordinal)
           || string.Equals(scope, "project", StringComparison.Ordinal)
           || string.Equals(scope, "solution", StringComparison.Ordinal);

    public static Document? FindDocument(this Solution solution, string filePath)
        => solution.Projects.SelectMany(static project => project.Documents)
            .FirstOrDefault(d => d.FilePath.MatchesByNormalizedPath(filePath));

    public static TextSpan CreateSelectionSpan(this int position, int? selectionStart, int? selectionLength)
    {
        if (selectionStart.HasValue && selectionLength.HasValue)
            return new TextSpan(selectionStart.Value, selectionLength.Value);

        return new TextSpan(position, 0);
    }

    public static bool IntersectsSelection(this TextSpan span, int? selectionStart, int? selectionLength)
    {
        if (!selectionStart.HasValue || !selectionLength.HasValue)
            return true;

        var selection = new TextSpan(selectionStart.Value, selectionLength.Value);
        return selection.OverlapsWith(span) || selection.Contains(span.Start) || span.Contains(selection.Start);
    }

    public static string EncodeKey(this string? value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    public static string DecodeKey(this string encoded)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    public static string? NormalizeNullable(this string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    public static string BuildFixId(this int workspaceVersion, string diagnosticId, int spanStart, int spanLength, string filePath)
    {
        var encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(filePath));
        return string.Join('|', "v1", workspaceVersion, RefactoringOperationOrchestrator.SupportedFixOperation, diagnosticId, spanStart, spanLength, encodedPath);
    }

    public static ParsedFixId? ParseFixId(this string fixId)
    {
        if (string.IsNullOrWhiteSpace(fixId))
        {
            return null;
        }

        var parts = fixId.Split('|');
        if (parts.Length != 7)
        {
            return null;
        }

        if (!string.Equals(parts[0], "v1", StringComparison.Ordinal)
            || !string.Equals(parts[2], RefactoringOperationOrchestrator.SupportedFixOperation, StringComparison.Ordinal)
            || !int.TryParse(parts[1], out var version)
            || !int.TryParse(parts[4], out var spanStart)
            || !int.TryParse(parts[5], out var spanLength))
        {
            return null;
        }

        string filePath;
        try
        {
            filePath = Encoding.UTF8.GetString(Convert.FromBase64String(parts[6]));
        }
        catch (FormatException)
        {
            return null;
        }

        return new ParsedFixId(version, parts[3], spanStart, spanLength, filePath);
    }

    public static string BuildProviderCodeFixKey(this string providerType, string diagnosticId, string? equivalenceKey, string title)
        => string.Join('|', "cf", providerType.EncodeKey(), diagnosticId.EncodeKey(), equivalenceKey.EncodeKey(), title.EncodeKey());

    public static string BuildProviderRefactoringKey(this string providerType, string? equivalenceKey, string title)
        => string.Join('|', "rf", providerType.EncodeKey(), equivalenceKey.EncodeKey(), title.EncodeKey());

    public static bool TryParseProviderCodeFixKey(this string key, out ProviderCodeFixKey parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var parts = key.Split('|');
        if (parts.Length != 5 || !string.Equals(parts[0], "cf", StringComparison.Ordinal))
            return false;

        var providerType = parts[1].DecodeKey();
        var diagnosticId = parts[2].DecodeKey();
        if (string.IsNullOrWhiteSpace(providerType) || string.IsNullOrWhiteSpace(diagnosticId))
            return false;

        parsed = new ProviderCodeFixKey(providerType, diagnosticId, parts[3].DecodeKey().NormalizeNullable(), parts[4].DecodeKey());
        return true;
    }

    public static bool TryParseProviderRefactoringKey(this string key, out ProviderRefactoringKey parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var parts = key.Split('|');
        if (parts.Length != 4 || !string.Equals(parts[0], "rf", StringComparison.Ordinal))
            return false;

        var providerType = parts[1].DecodeKey();
        if (string.IsNullOrWhiteSpace(providerType))
            return false;

        parsed = new ProviderRefactoringKey(providerType, parts[2].DecodeKey().NormalizeNullable(), parts[3].DecodeKey());
        return true;
    }

    public static async Task<IReadOnlyList<ChangedFilePreview>> CollectChangedFilesAsync(this Solution original, Solution updated, CancellationToken ct)
    {
        var changedDocumentIds = updated.GetChanges(original)
            .GetProjectChanges()
            .SelectMany(static project => project.GetChangedDocuments())
            .Distinct()
            .ToArray();

        var changed = new List<ChangedFilePreview>(changedDocumentIds.Length);
        foreach (var documentId in changedDocumentIds)
        {
            ct.ThrowIfCancellationRequested();
            var originalDoc = original.GetDocument(documentId);
            var updatedDoc = updated.GetDocument(documentId);
            var filePath = updatedDoc?.FilePath ?? updatedDoc?.Name ?? originalDoc?.FilePath ?? originalDoc?.Name ?? string.Empty;
            var editCount = 0;
            if (originalDoc != null && updatedDoc != null)
            {
                var originalText = await originalDoc.GetTextAsync(ct).ConfigureAwait(false);
                var updatedText = await updatedDoc.GetTextAsync(ct).ConfigureAwait(false);
                editCount = updatedText.GetTextChanges(originalText).Count;
            }

            changed.Add(new ChangedFilePreview(filePath, editCount));
        }

        return changed
            .Where(static file => !string.IsNullOrWhiteSpace(file.FilePath))
            .OrderBy(static file => file.FilePath, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> BuildCleanupMetadataWarnings(bool healthCheckPerformed, bool autoReloadAttempted, bool autoReloadSucceeded)
        =>
        [
            $"meta.{RefactoringOperationOrchestrator.CleanupHealthCheckPerformedDetail}={healthCheckPerformed.ToString().ToLowerInvariant()}",
            $"meta.{RefactoringOperationOrchestrator.CleanupAutoReloadAttemptedDetail}={autoReloadAttempted.ToString().ToLowerInvariant()}",
            $"meta.{RefactoringOperationOrchestrator.CleanupAutoReloadSucceededDetail}={autoReloadSucceeded.ToString().ToLowerInvariant()}"
        ];

    public static ExecuteCleanupResult CreateStaleWorkspaceResult(
        string scope,
        bool healthCheckPerformed,
        bool autoReloadAttempted,
        bool autoReloadSucceeded,
        int missingFileCount,
        string? reloadErrorCode = null)
    {
        return new ExecuteCleanupResult(
            scope,
            Array.Empty<string>(),
            Array.Empty<string>(),
            BuildCleanupMetadataWarnings(healthCheckPerformed, autoReloadAttempted, autoReloadSucceeded),
            CreateError(
                Core.ErrorCodes.StaleWorkspaceSnapshot,
                RefactoringOperationOrchestrator.CleanupStaleWorkspaceMessage,
                ("operation", "execute_cleanup"),
                (RefactoringOperationOrchestrator.CleanupHealthCheckPerformedDetail, healthCheckPerformed.ToString().ToLowerInvariant()),
                (RefactoringOperationOrchestrator.CleanupAutoReloadAttemptedDetail, autoReloadAttempted.ToString().ToLowerInvariant()),
                (RefactoringOperationOrchestrator.CleanupAutoReloadSucceededDetail, autoReloadSucceeded.ToString().ToLowerInvariant()),
                (RefactoringOperationOrchestrator.CleanupMissingFileCountDetail, missingFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (RefactoringOperationOrchestrator.CleanupReloadErrorCodeDetail, reloadErrorCode)));
    }

    public static ErrorInfo CreateError(string code, string message, params (string Key, string? Value)[] details)
    {
        if (details.Length == 0)
            return new ErrorInfo(code, message);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in details)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                map[key] = value;
        }

        return map.Count == 0 ? new ErrorInfo(code, message) : new ErrorInfo(code, message, map);
    }

    public static RenameSymbolResult CreateErrorResult(string code, string message, params (string Key, string? Value)[] details)
        => new(null, 0, Array.Empty<SourceLocation>(), Array.Empty<string>(), CreateError(code, message, details));

    public static RenameSymbolResult CreateErrorResult(ErrorInfo? error)
    {
        var safeError = error ?? new ErrorInfo(Core.ErrorCodes.InternalError, "An unknown error occurred while renaming a symbol.");
        return new RenameSymbolResult(null, 0, Array.Empty<SourceLocation>(), Array.Empty<string>(), safeError);
    }

    public static FormatDocumentResult CreateFormatDocumentErrorResult(string path, string code, string message, params (string Key, string? Value)[] details)
        => new(path, false, CreateError(code, message, details));

    public static FormatDocumentResult CreateFormatDocumentErrorResult(string path, ErrorInfo? error)
    {
        var safeError = error ?? new ErrorInfo(Core.ErrorCodes.InternalError, "An unknown error occurred while formatting the document.");
        return new FormatDocumentResult(path, false, safeError);
    }

    public static AddMethodResult CreateAddMethodErrorResult(string targetTypeSymbolId, string code, string message, params (string Key, string? Value)[] details)
        => new(
            "failed",
            Array.Empty<string>(),
            targetTypeSymbolId,
            null,
            new DiagnosticsDeltaInfo(Array.Empty<MutationDiagnosticInfo>(), Array.Empty<MutationDiagnosticInfo>()),
            CreateError(code, message, details));

    public static AddMethodResult CreateAddMethodErrorResult(string targetTypeSymbolId, ErrorInfo? error)
    {
        var safeError = error ?? new ErrorInfo(Core.ErrorCodes.InternalError, "An unknown error occurred while adding the method.");
        return new AddMethodResult("failed", [], targetTypeSymbolId, null, new DiagnosticsDeltaInfo([], []), safeError);
    }

    public static DeleteMethodResult CreateDeleteMethodErrorResult(string targetMethodSymbolId, string code, string message, params (string Key, string? Value)[] details)
        => new("failed", [], targetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), CreateError(code, message, details));

    public static DeleteMethodResult CreateDeleteMethodErrorResult(string targetMethodSymbolId, ErrorInfo? error)
    {
        var safeError = error ?? new ErrorInfo(Core.ErrorCodes.InternalError, "An unknown error occurred while deleting the method.");
        return new DeleteMethodResult("failed", [], targetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), safeError);
    }

    public static ReplaceMethodResult CreateReplaceMethodErrorResult(string targetMethodSymbolId, string code, string message, params (string Key, string? Value)[] details)
        => new("failed", [], targetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), CreateError(code, message, details));

    public static ReplaceMethodResult CreateReplaceMethodErrorResult(string targetMethodSymbolId, ErrorInfo? error)
    {
        var safeError = error ?? new ErrorInfo(Core.ErrorCodes.InternalError, "An unknown error occurred while replacing the method.");
        return new ReplaceMethodResult("failed", [], targetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), safeError);
    }

    public static ReplaceMethodBodyResult CreateReplaceMethodBodyErrorResult(string targetMethodSymbolId, string code, string message, params (string Key, string? Value)[] details)
        => new("failed", [], targetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), CreateError(code, message, details));

    public static ReplaceMethodBodyResult CreateReplaceMethodBodyErrorResult(string targetMethodSymbolId, ErrorInfo? error)
    {
        var safeError = error ?? new ErrorInfo(Core.ErrorCodes.InternalError, "An unknown error occurred while replacing the method body.");
        return new ReplaceMethodBodyResult("failed", [], targetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), safeError);
    }

    public static ErrorInfo? TryCreateInvalidSymbolIdError(string symbolId, string operation)
    {
        if (!string.IsNullOrWhiteSpace(symbolId))
            return null;

        return CreateError(
            Core.ErrorCodes.InvalidInput,
            "symbolId must be a non-empty, non-whitespace string.",
            ("parameter", "symbolId"),
            ("operation", operation));
    }

    public static ErrorInfo? TryCreateInvalidTargetTypeSymbolIdError(string symbolId, string operation)
    {
        if (!string.IsNullOrWhiteSpace(symbolId))
            return null;

        return CreateError(
            Core.ErrorCodes.InvalidInput,
            "targetTypeSymbolId must be a non-empty, non-whitespace string.",
            ("parameter", "targetTypeSymbolId"),
            ("operation", operation));
    }

    public static ErrorInfo? TryCreateInvalidMethodSymbolIdError(string symbolId, string operation)
    {
        if (!string.IsNullOrWhiteSpace(symbolId))
            return null;

        return CreateError(
            Core.ErrorCodes.InvalidInput,
            "targetMethodSymbolId must be a non-empty, non-whitespace string.",
            ("parameter", "targetMethodSymbolId"),
            ("operation", operation));
    }

    public static string NormalizeAcceptedSymbolIdForOutput(this string symbolId)
    {
        if (string.IsNullOrWhiteSpace(symbolId))
            return string.Empty;

        if (symbolId.TryToInternal(out _))
            return symbolId;

        if (!LooksLikeInternalSymbolId(symbolId))
            return symbolId;

        return symbolId.ToExternal();
    }

    private static bool LooksLikeInternalSymbolId(string symbolId)
        => symbolId.IndexOfAny([' ', ':', ';', ',', '(', ')', '<', '>', '[', ']', '{', '}', '|']) >= 0;

    public static PreviewCodeFixResult CreatePreviewError(string code, string message)
        => CreatePreviewError(CreateError(code, message, ("operation", "preview_code_fix")));

    public static PreviewCodeFixResult CreatePreviewError(ErrorInfo error)
        => new(string.Empty, string.Empty, [], error);

    public static ApplyCodeFixResult CreateApplyError(string fixId, string code, string message)
        => new(fixId, 0, [], CreateError(code, message, ("fixId", fixId), ("operation", "apply_code_fix")));

    public static bool MatchesProviderAction(this ActionExecutionIdentity identity, CodeAction action, string actionTitle)
    {
        if (!string.IsNullOrWhiteSpace(identity.RefactoringId) && string.Equals(identity.RefactoringId, action.EquivalenceKey, StringComparison.Ordinal))
            return true;

        return string.Equals(action.Title, actionTitle, StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> BuildCleanupRuleIds() =>
        [
            RefactoringOperationOrchestrator.CleanupRuleRemoveUnusedUsings,
            RefactoringOperationOrchestrator.CleanupRuleOrganizeUsings,
            RefactoringOperationOrchestrator.CleanupRuleFixModifierOrder,
            RefactoringOperationOrchestrator.CleanupRuleAddReadonly,
            RefactoringOperationOrchestrator.CleanupRuleFormat
        ];
}
