using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Diagnostics;

namespace RoslynMcp.Infrastructure.Refactoring;

internal sealed class RefactoringOperationOrchestrator : IRefactoringOperationOrchestrator
{
    internal const string SupportedFixOperation = "remove_unused_local";
    internal const string CleanupRuleRemoveUnusedUsings = "remove_unused_usings";
    internal const string CleanupRuleOrganizeUsings = "organize_usings";
    internal const string CleanupRuleFixModifierOrder = "fix_modifier_order";
    internal const string CleanupRuleAddReadonly = "add_readonly";
    internal const string CleanupRuleFormat = "format";
    internal const string CleanupHealthCheckPerformedDetail = "healthCheckPerformed";
    internal const string CleanupAutoReloadAttemptedDetail = "autoReloadAttempted";
    internal const string CleanupAutoReloadSucceededDetail = "autoReloadSucceeded";
    internal const string CleanupMissingFileCountDetail = "missingFileCount";
    internal const string CleanupReloadErrorCodeDetail = "reloadErrorCode";
    internal const string CleanupStaleWorkspaceMessage = "Workspace snapshot is stale relative to filesystem. Run reload_solution or load_solution, then retry cleanup.";
    internal const string SupportedFixCategory = "compiler";
    internal const string RefactoringOperationUseVar = "use_var_for_local";
    internal const string OriginRoslynatorCodeFix = "roslynator_codefix";
    internal const string OriginRoslynatorRefactoring = "roslynator_refactoring";
    internal const string PolicyProfileDefault = "default";
    internal const string RefactoringCategoryDefault = "refactoring";
    internal const string RefactoringActionPipelineFlowLog = "refactoring_action_pipeline_flow";

    internal static readonly HashSet<string> SupportedFixDiagnosticIds =
        new(StringComparer.OrdinalIgnoreCase) { "CS0168", "CS0219", "IDE0059" };

    internal static readonly HashSet<string> CleanupRemoveUnusedUsingDiagnostics =
        new(StringComparer.OrdinalIgnoreCase) { "IDE0005", "CS8019" };

    internal static readonly HashSet<string> CleanupModifierOrderDiagnostics =
        new(StringComparer.OrdinalIgnoreCase) { "IDE0036" };

    internal static readonly HashSet<string> CleanupReadonlyDiagnostics =
        new(StringComparer.OrdinalIgnoreCase) { "IDE0044" };

    internal readonly IRoslynSolutionAccessor _solutionAccessor;
    internal readonly ILogger<RoslynRefactoringService> _logger;
    internal readonly ActionIdentityService _actionIdentityService;
    internal readonly RefactoringPolicyService _refactoringPolicyService;
    internal readonly RoslynatorProviderCatalogService _providerCatalogService;
    private readonly RefactoringActionOperations _refactoringActions;
    private readonly CodeFixOperations _codeFixOperations;
    private readonly CleanupOperations _cleanupOperations;
    private readonly RenameOperations _renameOperations;
    private readonly OrganizeUsingsOperations _organizeUsingsOperations;

    public RefactoringOperationOrchestrator(IRoslynSolutionAccessor solutionAccessor,
        ILogger<RoslynRefactoringService>? logger = null)
    {
        _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));
        _logger = logger ?? NullLogger<RoslynRefactoringService>.Instance;
        _actionIdentityService = new ActionIdentityService();
        _refactoringPolicyService = new RefactoringPolicyService();
        _providerCatalogService = new RoslynatorProviderCatalogService();
        _refactoringActions = new RefactoringActionOperations(this);
        _codeFixOperations = new CodeFixOperations(this);
        _cleanupOperations = new CleanupOperations(this);
        _renameOperations = new RenameOperations(this);
        _organizeUsingsOperations = new OrganizeUsingsOperations(this);
    }

    public Task<GetRefactoringsAtPositionResult> GetRefactoringsAtPositionAsync(
        GetRefactoringsAtPositionRequest request,
        CancellationToken ct)
        => _refactoringActions.GetRefactoringsAtPositionAsync(request, ct);

    public Task<PreviewRefactoringResult> PreviewRefactoringAsync(PreviewRefactoringRequest request, CancellationToken ct)
        => _refactoringActions.PreviewRefactoringAsync(request, ct);

    public Task<ApplyRefactoringResult> ApplyRefactoringAsync(ApplyRefactoringRequest request, CancellationToken ct)
        => _refactoringActions.ApplyRefactoringAsync(request, ct);

    public Task<GetCodeFixesResult> GetCodeFixesAsync(GetCodeFixesRequest request, CancellationToken ct)
        => _codeFixOperations.GetCodeFixesAsync(request, ct);

    public Task<PreviewCodeFixResult> PreviewCodeFixAsync(PreviewCodeFixRequest request, CancellationToken ct)
        => _codeFixOperations.PreviewCodeFixAsync(request, ct);

    public Task<ApplyCodeFixResult> ApplyCodeFixAsync(ApplyCodeFixRequest request, CancellationToken ct)
        => _codeFixOperations.ApplyCodeFixAsync(request, ct);

    public Task<ExecuteCleanupResult> ExecuteCleanupAsync(ExecuteCleanupRequest request, CancellationToken ct)
        => _cleanupOperations.ExecuteCleanupAsync(request, ct);

    public Task<RenameSymbolResult> RenameSymbolAsync(RenameSymbolRequest request, CancellationToken ct)
        => _renameOperations.RenameSymbolAsync(request, ct);

    public Task<OrganizeUsingsResult> OrganizeUsingsAsync(OrganizeUsingsRequest request, CancellationToken ct)
        => _organizeUsingsOperations.OrganizeUsingsAsync(request, ct);

    internal async Task<Solution> ApplyDiagnosticCleanupStepAsync(
        Solution solution,
        IReadOnlyList<Document> scopeDocuments,
        ISet<string> allowedDiagnosticIds,
        CancellationToken ct)
    {
        var updated = solution;
        for (var pass = 0; pass < 3; pass++)
        {
            var changedInPass = false;
            foreach (var baseDocument in scopeDocuments)
            {
                ct.ThrowIfCancellationRequested();
                var document = updated.GetDocument(baseDocument.Id);
                if (document == null)
                {
                    continue;
                }

                var diagnostics = await GetProviderDiagnosticsForDocumentAsync(document, ct).ConfigureAwait(false);
                var candidates = diagnostics
                    .Where(d => d.Location.IsInSource && allowedDiagnosticIds.Contains(d.Id))
                    .OrderBy(static d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
                    .ThenBy(static d => d.Location.SourceSpan.Start)
                    .ThenBy(static d => d.Location.SourceSpan.Length)
                    .ThenBy(static d => d.Id, StringComparer.Ordinal)
                    .ToArray();

                foreach (var diagnostic in candidates)
                {
                    var actions = await CollectCodeFixActionsAsync(document, diagnostic, ct).ConfigureAwait(false);
                    var action = actions
                        .OrderBy(static candidate => candidate.ProviderTypeName, StringComparer.Ordinal)
                        .ThenBy(static candidate => candidate.Action.Title, StringComparer.Ordinal)
                        .ThenBy(static candidate => candidate.Action.EquivalenceKey ?? string.Empty, StringComparer.Ordinal)
                        .Select(static candidate => candidate.Action)
                        .FirstOrDefault();
                    if (action == null)
                    {
                        continue;
                    }

                    var applied = await TryApplyCodeActionToSolutionAsync(updated, action, ct).ConfigureAwait(false);
                    if (applied == null)
                    {
                        continue;
                    }

                    updated = applied;
                    document = updated.GetDocument(baseDocument.Id);
                    changedInPass = true;
                }
            }

            if (!changedInPass)
            {
                break;
            }
        }

        return updated;
    }

    internal async Task<Solution> OrganizeUsingsAsync(Solution solution, IReadOnlyList<Document> scopeDocuments, CancellationToken ct)
    {
        var updated = solution;
        foreach (var baseDocument in scopeDocuments)
        {
            ct.ThrowIfCancellationRequested();
            var document = updated.GetDocument(baseDocument.Id);
            if (document == null)
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root is not CompilationUnitSyntax compilationUnit)
            {
                continue;
            }

            var organizedRoot = compilationUnit.OrganizeUsings();
            if (organizedRoot.IsEquivalentTo(compilationUnit))
            {
                continue;
            }

            updated = updated.WithDocumentSyntaxRoot(document.Id, organizedRoot);
        }

        return updated;
    }

    internal async Task<Solution> FormatScopeAsync(Solution solution, IReadOnlyList<Document> scopeDocuments, CancellationToken ct)
    {
        var updated = solution;
        foreach (var baseDocument in scopeDocuments)
        {
            ct.ThrowIfCancellationRequested();
            var document = updated.GetDocument(baseDocument.Id);
            if (document == null)
            {
                continue;
            }

            var formatted = await Formatter.FormatAsync(document, cancellationToken: ct).ConfigureAwait(false);
            updated = formatted.Project.Solution;
        }

        return updated;
    }

    internal async Task<(Solution? Solution, ErrorInfo? Error)> TryGetSolutionAsync(CancellationToken ct)
    {
        try
        {
            return await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to access solution state for rename");
            return (null, new ErrorInfo(ErrorCodes.InternalError, "Unable to access the current solution."));
        }
    }

    internal async Task<ISymbol?> ResolveSymbolAsync(string symbolId, Solution solution, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbolId))
        {
            return null;
        }

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                continue;
            }

            var resolved = RefactoringSymbolIdentity.Resolve(symbolId, compilation, ct);
            if (resolved != null)
            {
                return resolved.OriginalDefinition ?? resolved;
            }
        }

        return null;
    }

    internal async Task<ISymbol?> TryResolveRenamedSymbolAsync(Solution solution,
        string newName,
        ISet<string> originalDeclarationKeys,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newName) || originalDeclarationKeys.Count == 0)
        {
            return null;
        }

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var candidates = await SymbolFinder.FindDeclarationsAsync(project, newName, ignoreCase: false,
                    SymbolFilter.TypeAndMember, ct)
                .ConfigureAwait(false);

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = candidate.OriginalDefinition ?? candidate;
                foreach (var sourceLocation in normalizedCandidate.Locations.Where(static l => l.IsInSource)
                             .Select(static location => location.ToSourceLocation()))
                {
                    if (originalDeclarationKeys.Contains(sourceLocation.GetLocationKey()))
                    {
                        return normalizedCandidate;
                    }
                }
            }
        }

        return null;
    }

    internal async Task<(Solution? Solution, int Version, ErrorInfo? Error)> TryGetSolutionWithVersionAsync(CancellationToken ct)
    {
        var (solution, solutionError) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return (null, 0, solutionError);
        }

        try
        {
            var (version, versionError) = await _solutionAccessor.GetWorkspaceVersionAsync(ct).ConfigureAwait(false);
            if (versionError != null)
            {
                return (null, 0, versionError);
            }

            return (solution, version, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read workspace version");
            return (null, 0, new ErrorInfo(ErrorCodes.InternalError, "Unable to access workspace version."));
        }
    }

    internal async Task<FixOperation?> TryBuildFixOperationAsync(Solution solution, ParsedFixId fix, CancellationToken ct)
    {
        if (!SupportedFixDiagnosticIds.Contains(fix.DiagnosticId))
        {
            return null;
        }

        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => d.FilePath.MatchesByNormalizedPath(fix.FilePath));
        if (document == null)
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root == null)
        {
            return null;
        }

        var declaration = root.FindNode(new TextSpan(fix.SpanStart, fix.SpanLength)) as LocalDeclarationStatementSyntax;
        if (declaration == null)
        {
            return null;
        }

        var variableName = declaration.Declaration.Variables[0].Identifier.ValueText;
        var title = $"Remove unused local variable '{variableName}'";
        return new FixOperation(
            title,
            async (currentSolution, cancellationToken) =>
            {
                var currentDocument = currentSolution.GetDocument(document.Id);
                if (currentDocument == null)
                {
                    return currentSolution;
                }

                var currentRoot = await currentDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (currentRoot == null)
                {
                    return currentSolution;
                }

                var currentDeclaration = currentRoot.FindNode(new TextSpan(fix.SpanStart, fix.SpanLength)) as LocalDeclarationStatementSyntax;
                if (currentDeclaration == null)
                {
                    return currentSolution;
                }

                var updatedRoot = currentRoot.RemoveNode(currentDeclaration, SyntaxRemoveOptions.KeepNoTrivia);
                if (updatedRoot == null)
                {
                    return currentSolution;
                }

                return currentSolution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
            });
    }

    internal async Task<IReadOnlyList<DiscoveredAction>> DiscoverActionsAtPositionAsync(
        Document document,
        int position,
        int? selectionStart,
        int? selectionLength,
        CancellationToken ct)
    {
        var discovered = new List<DiscoveredAction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var filePath = document.FilePath ?? document.Name;
        var selectionSpan = position.CreateSelectionSpan(selectionStart, selectionLength);

        var providerDiagnostics = await GetProviderDiagnosticsForDocumentAsync(document, ct).ConfigureAwait(false);
        foreach (var diagnostic in providerDiagnostics)
        {
            ct.ThrowIfCancellationRequested();
            if (!diagnostic.Location.IsInSource)
            {
                continue;
            }

            var span = diagnostic.Location.SourceSpan;
            if (!span.Contains(position) || !span.IntersectsSelection(selectionStart, selectionLength))
            {
                continue;
            }

            var fixes = await CollectCodeFixActionsAsync(document, diagnostic, ct).ConfigureAwait(false);
            foreach (var fix in fixes)
            {
                var providerKey = fix.ProviderTypeName.BuildProviderCodeFixKey(diagnostic.Id, fix.Action.EquivalenceKey, fix.Action.Title);
                var category = diagnostic.GetCodeFixCategory();
                var key = string.Join('|', filePath, span.Start, span.Length, fix.Action.Title, category, providerKey);
                if (!seen.Add(key))
                {
                    continue;
                }

                discovered.Add(new DiscoveredAction(
                    fix.Action.Title,
                    category,
                    OriginRoslynatorCodeFix,
                    providerKey,
                    filePath,
                    span.Start,
                    span.Length,
                    diagnostic.Location.ToSourceLocation(),
                    diagnostic.Id,
                    fix.Action.EquivalenceKey.NormalizeNullable()));
            }
        }

        foreach (var action in await CollectCodeRefactoringActionsAsync(document, selectionSpan, ct).ConfigureAwait(false))
        {
            var span = selectionSpan;
            var providerKey = action.ProviderTypeName.BuildProviderRefactoringKey(action.Action.EquivalenceKey, action.Action.Title);
            var key = string.Join('|', filePath, span.Start, span.Length, action.Action.Title, RefactoringCategoryDefault, providerKey);
            if (!seen.Add(key))
            {
                continue;
            }

            discovered.Add(new DiscoveredAction(
                action.Action.Title,
                RefactoringCategoryDefault,
                OriginRoslynatorRefactoring,
                providerKey,
                filePath,
                span.Start,
                span.Length,
                await document.ToSourceLocationAsync(span, ct).ConfigureAwait(false),
                null,
                action.Action.EquivalenceKey.NormalizeNullable()));
        }

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel != null)
        {
            foreach (var diagnostic in semanticModel.GetDiagnostics()
                         .Where(static d => d.Location.IsInSource)
                         .OrderBy(static d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
                         .ThenBy(static d => d.Location.SourceSpan.Start)
                         .ThenBy(static d => d.Location.SourceSpan.Length)
                         .ThenBy(static d => d.Id, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (!diagnostic.IsSupportedDiagnostic())
                {
                    continue;
                }

                var declaration = await document.TryGetUnusedLocalDeclarationAsync(diagnostic, ct).ConfigureAwait(false);
                if (declaration == null)
                {
                    continue;
                }

                var span = declaration.Span;
                if (!span.Contains(position) || !span.IntersectsSelection(selectionStart, selectionLength))
                {
                    continue;
                }

                var variableName = declaration.Declaration.Variables[0].Identifier.ValueText;
                var title = $"Remove unused local variable '{variableName}'";
                var providerKey = $"{SupportedFixOperation}:{diagnostic.Id}";
                var key = string.Join('|', filePath, span.Start, span.Length, title, SupportedFixCategory, providerKey);
                if (!seen.Add(key))
                {
                    continue;
                }

                discovered.Add(new DiscoveredAction(
                    title,
                    SupportedFixCategory,
                    OriginRoslynatorCodeFix,
                    providerKey,
                    filePath,
                    span.Start,
                    span.Length,
                    diagnostic.Location.ToSourceLocation(),
                    diagnostic.Id,
                    null));
            }
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var token = root?.FindToken(position);
        var localDeclaration = token?.Parent?.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (localDeclaration != null
            && !localDeclaration.IsConst
            && localDeclaration.Declaration.Variables.Count == 1
            && localDeclaration.Declaration.Type is not IdentifierNameSyntax { Identifier.ValueText: "var" })
        {
            var typeSpan = localDeclaration.Declaration.Type.Span;
            if (typeSpan.IntersectsSelection(selectionStart, selectionLength))
            {
                var key = string.Join('|', filePath, typeSpan.Start, typeSpan.Length, RefactoringOperationUseVar);
                if (seen.Add(key))
                {
                    discovered.Add(new DiscoveredAction(
                        "Use 'var' for local declaration",
                        "style",
                        OriginRoslynatorRefactoring,
                        RefactoringOperationUseVar,
                        filePath,
                        typeSpan.Start,
                        typeSpan.Length,
                        localDeclaration.GetLocation().ToSourceLocation(),
                        null,
                        RefactoringOperationUseVar));
                }
            }
        }

        return discovered;
    }

    internal async Task<FixOperation?> TryBuildActionOperationAsync(Solution solution, ActionExecutionIdentity identity, CancellationToken ct)
    {
        if (identity.ProviderActionKey.TryParseProviderCodeFixKey(out var codeFixKey))
        {
            return await TryBuildProviderCodeFixOperationAsync(solution, identity, codeFixKey, ct).ConfigureAwait(false);
        }

        if (identity.ProviderActionKey.TryParseProviderRefactoringKey(out var refactoringKey))
        {
            return await TryBuildProviderRefactoringOperationAsync(solution, identity, refactoringKey, ct).ConfigureAwait(false);
        }

        if (string.Equals(identity.ProviderActionKey, RefactoringOperationUseVar, StringComparison.Ordinal))
        {
            return await TryBuildUseVarOperationAsync(solution, identity, ct).ConfigureAwait(false);
        }

        if (identity.ProviderActionKey.StartsWith(SupportedFixOperation + ":", StringComparison.Ordinal))
        {
            var parsedFix = new ParsedFixId(identity.WorkspaceVersion,
                identity.DiagnosticId ?? string.Empty,
                identity.SpanStart,
                identity.SpanLength,
                identity.FilePath);
            return await TryBuildFixOperationAsync(solution, parsedFix, ct).ConfigureAwait(false);
        }

        return null;
    }

    internal async Task<FixOperation?> TryBuildUseVarOperationAsync(Solution solution, ActionExecutionIdentity identity, CancellationToken ct)
    {
        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => d.FilePath.MatchesByNormalizedPath(identity.FilePath));
        if (document == null)
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root == null)
        {
            return null;
        }

        var typeSyntax = root.FindNode(new TextSpan(identity.SpanStart, identity.SpanLength)) as TypeSyntax;
        if (typeSyntax == null)
        {
            return null;
        }

        return new FixOperation(
            "Use 'var' for local declaration",
            async (currentSolution, cancellationToken) =>
            {
                var currentDocument = currentSolution.GetDocument(document.Id);
                if (currentDocument == null)
                {
                    return currentSolution;
                }

                var currentRoot = await currentDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (currentRoot == null)
                {
                    return currentSolution;
                }

                var currentTypeSyntax = currentRoot.FindNode(new TextSpan(identity.SpanStart, identity.SpanLength)) as TypeSyntax;
                if (currentTypeSyntax == null)
                {
                    return currentSolution;
                }

                var replacement = SyntaxFactory.IdentifierName("var").WithTriviaFrom(currentTypeSyntax);
                var updatedRoot = currentRoot.ReplaceNode(currentTypeSyntax, replacement);
                return currentSolution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
            });
    }

    internal async Task<FixOperation?> TryBuildProviderCodeFixOperationAsync(Solution solution,
        ActionExecutionIdentity identity,
        ProviderCodeFixKey key,
        CancellationToken ct)
    {
        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => d.FilePath.MatchesByNormalizedPath(identity.FilePath));
        if (document == null)
        {
            return null;
        }

        var diagnostics = await GetProviderDiagnosticsForDocumentAsync(document, ct).ConfigureAwait(false);
        var matches = diagnostics
            .Where(d => d.Location.IsInSource
                        && string.Equals(d.Id, identity.DiagnosticId, StringComparison.Ordinal)
                        && d.Location.SourceSpan.Start == identity.SpanStart
                        && d.Location.SourceSpan.Length == identity.SpanLength)
            .OrderBy(static d => d.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var diagnostic in matches)
        {
            var actions = await CollectCodeFixActionsAsync(document, diagnostic, ct).ConfigureAwait(false);
            var selected = actions
                .Where(candidate => string.Equals(candidate.ProviderTypeName, key.ProviderTypeName, StringComparison.Ordinal))
                .Select(candidate => candidate.Action)
                .FirstOrDefault(action => identity.MatchesProviderAction(action, key.ActionTitle));
            if (selected == null)
            {
                continue;
            }

            return new FixOperation(
                selected.Title,
                async (currentSolution, cancellationToken) =>
                {
                    var currentDocument = currentSolution.FindDocument(identity.FilePath);
                    if (currentDocument == null)
                    {
                        return currentSolution;
                    }

                    var currentDiagnostics = await GetProviderDiagnosticsForDocumentAsync(currentDocument, cancellationToken).ConfigureAwait(false);
                    var currentDiagnostic = currentDiagnostics
                        .Where(d => d.Location.IsInSource
                                    && string.Equals(d.Id, identity.DiagnosticId, StringComparison.Ordinal)
                                    && d.Location.SourceSpan.Start == identity.SpanStart
                                    && d.Location.SourceSpan.Length == identity.SpanLength)
                        .OrderBy(static d => d.Id, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (currentDiagnostic == null)
                    {
                        return currentSolution;
                    }

                    var currentActions = await CollectCodeFixActionsAsync(currentDocument, currentDiagnostic, cancellationToken).ConfigureAwait(false);
                    var currentAction = currentActions
                        .Where(candidate => string.Equals(candidate.ProviderTypeName, key.ProviderTypeName, StringComparison.Ordinal))
                        .Select(candidate => candidate.Action)
                        .FirstOrDefault(action => identity.MatchesProviderAction(action, key.ActionTitle));

                    if (currentAction == null)
                    {
                        return currentSolution;
                    }

                    var applied = await TryApplyCodeActionToSolutionAsync(currentSolution, currentAction, cancellationToken).ConfigureAwait(false);
                    return applied ?? currentSolution;
                });
        }

        return null;
    }

    internal async Task<FixOperation?> TryBuildProviderRefactoringOperationAsync(Solution solution,
        ActionExecutionIdentity identity,
        ProviderRefactoringKey key,
        CancellationToken ct)
    {
        var document = solution.FindDocument(identity.FilePath);
        if (document == null)
        {
            return null;
        }

        var span = new TextSpan(identity.SpanStart, identity.SpanLength);
        var actions = await CollectCodeRefactoringActionsAsync(document, span, ct).ConfigureAwait(false);
        var selected = actions
            .Where(candidate => string.Equals(candidate.ProviderTypeName, key.ProviderTypeName, StringComparison.Ordinal))
            .Select(candidate => candidate.Action)
            .FirstOrDefault(action => identity.MatchesProviderAction(action, key.ActionTitle));
        if (selected == null)
        {
            return null;
        }

        return new FixOperation(
            selected.Title,
            async (currentSolution, cancellationToken) =>
            {
                var currentDocument = currentSolution.FindDocument(identity.FilePath);
                if (currentDocument == null)
                {
                    return currentSolution;
                }

                var currentActions = await CollectCodeRefactoringActionsAsync(currentDocument, span, cancellationToken).ConfigureAwait(false);
                var currentAction = currentActions
                    .Where(candidate => string.Equals(candidate.ProviderTypeName, key.ProviderTypeName, StringComparison.Ordinal))
                    .Select(candidate => candidate.Action)
                    .FirstOrDefault(action => identity.MatchesProviderAction(action, key.ActionTitle));
                if (currentAction == null)
                {
                    return currentSolution;
                }

                var applied = await TryApplyCodeActionToSolutionAsync(currentSolution, currentAction, cancellationToken).ConfigureAwait(false);
                return applied ?? currentSolution;
            });
    }

    internal async Task<IReadOnlyList<Diagnostic>> GetProviderDiagnosticsForDocumentAsync(Document document, CancellationToken ct)
    {
        var diagnostics = new List<Diagnostic>();
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel != null)
        {
            diagnostics.AddRange(semanticModel.GetDiagnostics()
                .Where(static d => d.Location.IsInSource));
        }

        var catalog = _providerCatalogService.Catalog;
        if (catalog.Error == null && !catalog.Analyzers.IsDefaultOrEmpty)
        {
            var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
            var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            if (compilation != null && tree != null)
            {
                var options = new CompilationWithAnalyzersOptions(
                    new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                    onAnalyzerException: null,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false);
                var withAnalyzers = compilation.WithAnalyzers(catalog.Analyzers, options);
                var analyzerDiagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);
                diagnostics.AddRange(analyzerDiagnostics.Where(d => d.Location.IsInSource && ReferenceEquals(d.Location.SourceTree, tree)));
            }
        }

        return diagnostics
            .OrderBy(static d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(static d => d.Location.SourceSpan.Start)
            .ThenBy(static d => d.Location.SourceSpan.Length)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToArray();
    }

    internal async Task<IReadOnlyList<ProviderCodeActionCandidate>> CollectCodeFixActionsAsync(Document document, Diagnostic diagnostic, CancellationToken ct)
    {
        var catalog = _providerCatalogService.Catalog;
        if (catalog.Error != null || catalog.CodeFixProviders.IsDefaultOrEmpty)
        {
            return Array.Empty<ProviderCodeActionCandidate>();
        }

        var candidates = new List<ProviderCodeActionCandidate>();
        foreach (var provider in catalog.CodeFixProviders.OrderBy(static p => p.GetType().FullName, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var registered = new List<CodeAction>();
            var context = new CodeFixContext(
                document,
                diagnostic,
                (action, _) =>
                {
                    if (action != null)
                    {
                        registered.Add(action);
                    }
                },
                ct);

            try
            {
                await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Code fix provider failed: {ProviderType}", provider.GetType().FullName);
                continue;
            }

            foreach (var action in registered.OrderBy(static a => a.Title, StringComparer.Ordinal)
                         .ThenBy(static a => a.EquivalenceKey ?? string.Empty, StringComparer.Ordinal))
            {
                candidates.Add(new ProviderCodeActionCandidate(provider.GetType().FullName ?? provider.GetType().Name, action));
            }
        }

        return candidates;
    }

    internal async Task<IReadOnlyList<ProviderCodeActionCandidate>> CollectCodeRefactoringActionsAsync(Document document, TextSpan selectionSpan, CancellationToken ct)
    {
        var catalog = _providerCatalogService.Catalog;
        if (catalog.Error != null || catalog.RefactoringProviders.IsDefaultOrEmpty)
        {
            return Array.Empty<ProviderCodeActionCandidate>();
        }

        var candidates = new List<ProviderCodeActionCandidate>();
        foreach (var provider in catalog.RefactoringProviders.OrderBy(static p => p.GetType().FullName, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var registered = new List<CodeAction>();
            var context = new CodeRefactoringContext(
                document,
                selectionSpan,
                action =>
                {
                    if (action != null)
                    {
                        registered.Add(action);
                    }
                },
                ct);
            try
            {
                await provider.ComputeRefactoringsAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Refactoring provider failed: {ProviderType}", provider.GetType().FullName);
                continue;
            }

            foreach (var action in registered.OrderBy(static a => a.Title, StringComparer.Ordinal)
                         .ThenBy(static a => a.EquivalenceKey ?? string.Empty, StringComparer.Ordinal))
            {
                candidates.Add(new ProviderCodeActionCandidate(provider.GetType().FullName ?? provider.GetType().Name, action));
            }
        }

        return candidates;
    }

    internal async Task<Solution?> TryApplyCodeActionToSolutionAsync(Solution currentSolution, CodeAction action, CancellationToken ct)
    {
        try
        {
            var operations = await action.GetOperationsAsync(ct).ConfigureAwait(false);
            var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
            return applyOperation?.ChangedSolution;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to apply provider action {ActionTitle}", action.Title);
            return null;
        }
    }

    internal void LogActionPipelineFlow(
        string operation,
        string actionOrigin,
        string actionType,
        string policyDecision,
        long startedAt,
        string resultCode,
        int affectedDocumentCount)
    {
        var duration = Stopwatch.GetElapsedTime(startedAt);
        _logger.LogInformation(
            "{EventName} operation={Operation} actionOrigin={ActionOrigin} actionType={ActionType} policyDecision={PolicyDecision} durationMs={DurationMs} resultCode={ResultCode} affectedDocumentCount={AffectedDocumentCount}",
            RefactoringActionPipelineFlowLog,
            operation,
            actionOrigin,
            actionType,
            policyDecision,
            duration.TotalMilliseconds,
            resultCode,
            affectedDocumentCount);
    }

}
