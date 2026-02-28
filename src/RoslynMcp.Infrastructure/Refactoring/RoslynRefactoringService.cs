using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Refactoring;
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
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace RoslynMcp.Infrastructure.Refactoring;

internal sealed class RefactoringOperationOrchestrator : IRefactoringOperationOrchestrator
{
    private const string SupportedFixOperation = "remove_unused_local";
    private const string CleanupRuleRemoveUnusedUsings = "remove_unused_usings";
    private const string CleanupRuleOrganizeUsings = "organize_usings";
    private const string CleanupRuleFixModifierOrder = "fix_modifier_order";
    private const string CleanupRuleAddReadonly = "add_readonly";
    private const string CleanupRuleFormat = "format";
    private const string CleanupHealthCheckPerformedDetail = "healthCheckPerformed";
    private const string CleanupAutoReloadAttemptedDetail = "autoReloadAttempted";
    private const string CleanupAutoReloadSucceededDetail = "autoReloadSucceeded";
    private const string CleanupMissingFileCountDetail = "missingFileCount";
    private const string CleanupReloadErrorCodeDetail = "reloadErrorCode";
    private const string CleanupStaleWorkspaceMessage = "Workspace snapshot is stale relative to filesystem. Run reload_solution or load_solution, then retry cleanup.";
    private const string SupportedFixCategory = "compiler";
    private const string RefactoringOperationUseVar = "use_var_for_local";
    private const string OriginRoslynatorCodeFix = "roslynator_codefix";
    private const string OriginRoslynatorRefactoring = "roslynator_refactoring";
    private const string PolicyProfileDefault = "default";
    private const string RefactoringCategoryDefault = "refactoring";
    private const string RefactoringActionPipelineFlowLog = "refactoring_action_pipeline_flow";
    private const string RoslynatorAnalyzerPackageId = "roslynator.analyzers";
    private const string RoslynatorAnalyzerPackageVersion = "4.15.0";
    private const string RoslynatorAnalyzerFilename = "Roslynator.CSharp.Analyzers.dll";
    private const string RoslynatorCodeFixesFilename = "Roslynator.CSharp.Analyzers.CodeFixes.dll";
    private const string RoslynatorCodeFixPathEnvVar = "RoslynMcp__RoslynatorCodeFixPath";

    private static readonly HashSet<string> SupportedFixDiagnosticIds =
        new(StringComparer.OrdinalIgnoreCase) { "CS0168", "CS0219", "IDE0059" };

    private static readonly HashSet<string> CleanupRemoveUnusedUsingDiagnostics =
        new(StringComparer.OrdinalIgnoreCase) { "IDE0005", "CS8019" };

    private static readonly HashSet<string> CleanupModifierOrderDiagnostics =
        new(StringComparer.OrdinalIgnoreCase) { "IDE0036" };

    private static readonly HashSet<string> CleanupReadonlyDiagnostics =
        new(StringComparer.OrdinalIgnoreCase) { "IDE0044" };

    private static readonly string[] RoslynatorAnalyzerRelativePathSegments =
        { "analyzers", "dotnet", "roslyn4.7", "cs", RoslynatorAnalyzerFilename };

    private static readonly string[] RoslynatorCodeFixRelativePathSegments =
        { "analyzers", "dotnet", "roslyn4.7", "cs", RoslynatorCodeFixesFilename };

    private static readonly Lazy<(ImmutableArray<DiagnosticAnalyzer> Analyzers, ImmutableArray<CodeFixProvider> CodeFixProviders, ImmutableArray<CodeRefactoringProvider> RefactoringProviders, Exception? Error)> s_providerCatalog =
        new(LoadProviderCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IRoslynSolutionAccessor _solutionAccessor;
    private readonly ILogger<RoslynRefactoringService> _logger;
    private readonly ActionIdentityService _actionIdentityService;
    private readonly RefactoringPolicyService _refactoringPolicyService;

    public RefactoringOperationOrchestrator(IRoslynSolutionAccessor solutionAccessor,
        ILogger<RoslynRefactoringService>? logger = null)
    {
        _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));
        _logger = logger ?? NullLogger<RoslynRefactoringService>.Instance;
        _actionIdentityService = new ActionIdentityService();
        _refactoringPolicyService = new RefactoringPolicyService();
    }

    public async Task<GetRefactoringsAtPositionResult> GetRefactoringsAtPositionAsync(
        GetRefactoringsAtPositionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        const string operation = "get_refactorings_at_position";
        string successCode;
        string actionOrigin = "none";
        string actionType = "discover";
        string policyDecision = "n/a";
        var affectedDocumentCount = 0;

        if (string.IsNullOrWhiteSpace(request.Path) || request.Line < 1 || request.Column < 1)
        {
            var invalid = new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                CreateError(ErrorCodes.InvalidInput,
                    "path, line, and column must be provided and valid.",
                    ("operation", "get_refactorings_at_position")));
            LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.InvalidInput, affectedDocumentCount);
            return invalid;
        }

        if ((request.SelectionStart.HasValue && request.SelectionStart.Value < 0)
            || (request.SelectionLength.HasValue && request.SelectionLength.Value < 0))
        {
            var invalidSelection = new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                CreateError(ErrorCodes.InvalidInput,
                    "selectionStart and selectionLength must be non-negative when provided.",
                    ("operation", "get_refactorings_at_position")));
            LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.InvalidInput, affectedDocumentCount);
            return invalidSelection;
        }

        var (solution, workspaceVersion, error) = await TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            successCode = error?.Code ?? ErrorCodes.InternalError;
            LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, successCode, affectedDocumentCount);
            return new GetRefactoringsAtPositionResult(Array.Empty<RefactoringActionDescriptor>(), error);
        }

        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => MatchesByNormalizedPath(d.FilePath, request.Path));
        if (document == null)
        {
            var pathError = new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                CreateError(ErrorCodes.PathOutOfScope,
                    "The provided path is outside the selected solution scope.",
                    ("path", request.Path),
                    ("operation", "get_refactorings_at_position")));
            LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.PathOutOfScope, affectedDocumentCount);
            return pathError;
        }

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        if (request.Line > text.Lines.Count)
        {
            var invalidLine = new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                CreateError(ErrorCodes.InvalidInput,
                    "line is outside document bounds.",
                    ("operation", "get_refactorings_at_position")));
            LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.InvalidInput, affectedDocumentCount);
            return invalidLine;
        }

        var line = text.Lines[request.Line - 1];
        var maxColumn = line.Span.Length + 1;
        if (request.Column > maxColumn)
        {
            var invalidColumn = new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                CreateError(ErrorCodes.InvalidInput,
                    "column is outside line bounds.",
                    ("operation", "get_refactorings_at_position")));
            LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.InvalidInput, affectedDocumentCount);
            return invalidColumn;
        }

        var selectionStart = request.SelectionStart;
        var selectionLength = request.SelectionLength;
        if (selectionStart.HasValue && selectionLength.HasValue && selectionStart.Value + selectionLength.Value > text.Length)
        {
            var invalidRange = new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                CreateError(ErrorCodes.InvalidInput,
                    "selection is outside document bounds.",
                    ("operation", "get_refactorings_at_position")));
            LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.InvalidInput, affectedDocumentCount);
            return invalidRange;
        }

        var position = line.Start + (request.Column - 1);
        var profile = string.IsNullOrWhiteSpace(request.PolicyProfile) ? PolicyProfileDefault : request.PolicyProfile.Trim();
        var discovered = await DiscoverActionsAtPositionAsync(document, position, selectionStart, selectionLength, ct).ConfigureAwait(false);
        var actions = discovered
            .OrderBy(static item => item.FilePath, StringComparer.Ordinal)
            .ThenBy(static item => item.SpanStart)
            .ThenBy(static item => item.SpanLength)
            .ThenBy(static item => item.Title, StringComparer.Ordinal)
            .ThenBy(static item => item.Category, StringComparer.Ordinal)
            .ThenBy(static item => item.ProviderActionKey, StringComparer.Ordinal)
            .Select(item =>
            {
                var policy = _refactoringPolicyService.Evaluate(item, profile);
                var actionId = _actionIdentityService.Create(workspaceVersion, profile, item);
                return new RefactoringActionDescriptor(
                    actionId,
                    item.Title,
                    item.Category,
                    item.Origin,
                    policy.RiskLevel,
                    new PolicyDecisionInfo(policy.Decision, policy.ReasonCode, policy.ReasonMessage),
                    item.Location,
                    item.DiagnosticId,
                    item.RefactoringId);
            })
            .ToArray();

        affectedDocumentCount = actions.Length;
        successCode = "ok";
        if (actions.Length > 0)
        {
            actionOrigin = actions[0].Origin;
            policyDecision = actions[0].PolicyDecision.Decision;
            actionType = actions[0].Category;
        }
        LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, successCode, affectedDocumentCount);

        return new GetRefactoringsAtPositionResult(actions);
    }

    public async Task<PreviewRefactoringResult> PreviewRefactoringAsync(PreviewRefactoringRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        const string operationName = "preview_refactoring";

        var identity = _actionIdentityService.Parse(request.ActionId);
        if (identity == null)
        {
            var invalid = new PreviewRefactoringResult(
                string.Empty,
                string.Empty,
                Array.Empty<ChangedFilePreview>(),
                CreateError(ErrorCodes.ActionNotFound,
                    "actionId is invalid or unsupported.",
                    ("operation", "preview_refactoring")));
            LogActionPipelineFlow(operationName, "unknown", "unknown", "n/a", startedAt, ErrorCodes.ActionNotFound, 0);
            return invalid;
        }

        var (solution, workspaceVersion, error) = await TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            LogActionPipelineFlow(operationName, identity.Origin, identity.Category, "n/a", startedAt, error?.Code ?? ErrorCodes.InternalError, 0);
            return new PreviewRefactoringResult(string.Empty, string.Empty, Array.Empty<ChangedFilePreview>(), error);
        }

        if (workspaceVersion != identity.WorkspaceVersion)
        {
            var stale = new PreviewRefactoringResult(
                request.ActionId,
                string.Empty,
                Array.Empty<ChangedFilePreview>(),
                CreateError(ErrorCodes.WorkspaceChanged,
                    "Workspace changed since actionId was produced.",
                    ("operation", "preview_refactoring")));
            LogActionPipelineFlow(operationName, identity.Origin, identity.Category, "n/a", startedAt, ErrorCodes.WorkspaceChanged, 0);
            return stale;
        }

        var actionOperation = await TryBuildActionOperationAsync(solution, identity, ct).ConfigureAwait(false);
        if (actionOperation == null)
        {
            var notFound = new PreviewRefactoringResult(
                request.ActionId,
                string.Empty,
                Array.Empty<ChangedFilePreview>(),
                CreateError(ErrorCodes.ActionNotFound,
                    "No matching refactoring action found for actionId.",
                    ("operation", "preview_refactoring")));
            LogActionPipelineFlow(operationName, identity.Origin, identity.Category, "n/a", startedAt, ErrorCodes.ActionNotFound, 0);
            return notFound;
        }

        var preview = await actionOperation.ApplyAsync(solution, ct).ConfigureAwait(false);
        var changedFiles = await CollectChangedFilesAsync(solution, preview, ct).ConfigureAwait(false);
        LogActionPipelineFlow(operationName, identity.Origin, identity.Category, "n/a", startedAt, "ok", changedFiles.Count);
        return new PreviewRefactoringResult(request.ActionId, actionOperation.Title, changedFiles);
    }

    public async Task<ApplyRefactoringResult> ApplyRefactoringAsync(ApplyRefactoringRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        const string operationName = "apply_refactoring";

        var identity = _actionIdentityService.Parse(request.ActionId);
        if (identity == null)
        {
            var invalid = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                CreateError(ErrorCodes.ActionNotFound,
                    "actionId is invalid or unsupported.",
                    ("operation", "apply_refactoring")));
            LogActionPipelineFlow(operationName, "unknown", "unknown", "n/a", startedAt, ErrorCodes.ActionNotFound, 0);
            return invalid;
        }

        var policy = _refactoringPolicyService.Evaluate(identity.ToDiscoveredAction(), identity.PolicyProfile);
        if (!string.Equals(policy.Decision, "allow", StringComparison.Ordinal))
        {
            var blocked = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                CreateError(ErrorCodes.PolicyBlocked,
                    policy.ReasonMessage,
                    ("operation", "apply_refactoring"),
                    ("policyDecision", policy.Decision),
                    ("policyReasonCode", policy.ReasonCode)));
            LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, ErrorCodes.PolicyBlocked, 0);
            return blocked;
        }

        var (solution, workspaceVersion, error) = await TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, error?.Code ?? ErrorCodes.InternalError, 0);
            return new ApplyRefactoringResult(request.ActionId, 0, Array.Empty<string>(), error);
        }

        if (workspaceVersion != identity.WorkspaceVersion)
        {
            var stale = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                CreateError(ErrorCodes.WorkspaceChanged,
                    "Workspace changed since actionId was produced.",
                    ("operation", "apply_refactoring")));
            LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, ErrorCodes.WorkspaceChanged, 0);
            return stale;
        }

        var actionOperation = await TryBuildActionOperationAsync(solution, identity, ct).ConfigureAwait(false);
        if (actionOperation == null)
        {
            var notFound = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                CreateError(ErrorCodes.ActionNotFound,
                    "No matching refactoring action found for actionId.",
                    ("operation", "apply_refactoring")));
            LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, ErrorCodes.ActionNotFound, 0);
            return notFound;
        }

        var updated = await actionOperation.ApplyAsync(solution, ct).ConfigureAwait(false);
        var changedFiles = await CollectChangedFilesAsync(solution, updated, ct).ConfigureAwait(false);
        if (changedFiles.Count == 0)
        {
            var conflict = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                CreateError(ErrorCodes.FixConflict,
                    "Refactoring produced no changes to apply.",
                    ("operation", "apply_refactoring")));
            LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, ErrorCodes.FixConflict, 0);
            return conflict;
        }

        var (applied, applyError) = await _solutionAccessor.TryApplySolutionAsync(updated, ct).ConfigureAwait(false);
        if (!applied)
        {
            var applyFailed = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                applyError ?? CreateError(ErrorCodes.InternalError, "Failed to apply refactoring changes."));
            LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, applyFailed.Error?.Code ?? ErrorCodes.InternalError, 0);
            return applyFailed;
        }

        var paths = changedFiles.Select(static item => item.FilePath).ToArray();
        LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, "ok", paths.Length);
        return new ApplyRefactoringResult(request.ActionId, paths.Length, paths);
    }

    public async Task<GetCodeFixesResult> GetCodeFixesAsync(GetCodeFixesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (!IsValidScope(request.Scope))
        {
            return new GetCodeFixesResult(Array.Empty<CodeFixDescriptor>(),
                CreateError(ErrorCodes.InvalidRequest,
                    "scope must be one of: document, project, solution.",
                    ("parameter", "scope"),
                    ("operation", "get_code_fixes")));
        }

        if (string.Equals(request.Scope, "document", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(request.Path))
        {
            return new GetCodeFixesResult(Array.Empty<CodeFixDescriptor>(),
                CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is document.",
                    ("parameter", "path"),
                    ("operation", "get_code_fixes")));
        }

        var (solution, version, error) = await TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new GetCodeFixesResult(Array.Empty<CodeFixDescriptor>(), error);
        }

        var documents = ResolveScopeDocuments(solution, request.Scope, request.Path).ToArray();
        if (documents.Length == 0)
        {
            return new GetCodeFixesResult(Array.Empty<CodeFixDescriptor>(),
                CreateError(ErrorCodes.PathOutOfScope,
                    "The provided path is outside the selected solution scope.",
                    ("path", request.Path),
                    ("operation", "get_code_fixes")));
        }

        var diagnosticFilter = CreateDiagnosticFilter(request.DiagnosticIds);
        var categoryFilter = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        var fixes = new List<CodeFixDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in documents.OrderBy(static d => d.FilePath ?? d.Name, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var diagnostics = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (diagnostics == null)
            {
                continue;
            }

            foreach (var diagnostic in diagnostics.GetDiagnostics()
                         .Where(static d => d.Location.IsInSource)
                         .OrderBy(static d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
                         .ThenBy(static d => d.Location.SourceSpan.Start)
                         .ThenBy(static d => d.Id, StringComparer.Ordinal))
            {
                if (!IsSupportedDiagnostic(diagnostic))
                {
                    continue;
                }

                if (diagnosticFilter != null && !diagnosticFilter.Contains(diagnostic.Id))
                {
                    continue;
                }

                if (categoryFilter != null && !string.Equals(SupportedFixCategory, categoryFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var declaration = await TryGetUnusedLocalDeclarationAsync(document, diagnostic, ct).ConfigureAwait(false);
                if (declaration == null)
                {
                    continue;
                }

                var fix = CreateFixDescriptor(document, diagnostic, declaration, version);
                if (seen.Add(fix.FixId))
                {
                    fixes.Add(fix);
                }
            }
        }

        var ordered = fixes
            .OrderBy(static f => f.FilePath, StringComparer.Ordinal)
            .ThenBy(static f => f.Location.Line)
            .ThenBy(static f => f.Location.Column)
            .ThenBy(static f => f.DiagnosticId, StringComparer.Ordinal)
            .ThenBy(static f => f.Title, StringComparer.Ordinal)
            .ThenBy(static f => f.FixId, StringComparer.Ordinal)
            .ToList();
        return new GetCodeFixesResult(ordered);
    }

    public async Task<PreviewCodeFixResult> PreviewCodeFixAsync(PreviewCodeFixRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var parse = ParseFixId(request.FixId);
        if (parse == null)
        {
            return CreatePreviewError(ErrorCodes.FixNotFound, "fixId is invalid or unsupported.");
        }

        var (solution, version, error) = await TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return CreatePreviewError(error ?? new ErrorInfo(ErrorCodes.InternalError, "Unable to access the current solution."));
        }

        if (version != parse.WorkspaceVersion)
        {
            return CreatePreviewError(ErrorCodes.WorkspaceChanged, "Workspace changed since fixId was produced.");
        }

        var operation = await TryBuildFixOperationAsync(solution, parse, ct).ConfigureAwait(false);
        if (operation == null)
        {
            return CreatePreviewError(ErrorCodes.FixNotFound, "No matching code fix found for fixId.");
        }

        var previewSolution = await operation.ApplyAsync(solution, ct).ConfigureAwait(false);
        var changedFiles = await CollectChangedFilesAsync(solution, previewSolution, ct).ConfigureAwait(false);

        return new PreviewCodeFixResult(
            request.FixId,
            operation.Title,
            changedFiles);
    }

    public async Task<ApplyCodeFixResult> ApplyCodeFixAsync(ApplyCodeFixRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var parse = ParseFixId(request.FixId);
        if (parse == null)
        {
            return CreateApplyError(request.FixId, ErrorCodes.FixNotFound, "fixId is invalid or unsupported.");
        }

        var (solution, version, error) = await TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ApplyCodeFixResult(request.FixId, 0, Array.Empty<string>(), error);
        }

        if (version != parse.WorkspaceVersion)
        {
            return CreateApplyError(request.FixId, ErrorCodes.WorkspaceChanged, "Workspace changed since fixId was produced.");
        }

        var operation = await TryBuildFixOperationAsync(solution, parse, ct).ConfigureAwait(false);
        if (operation == null)
        {
            return CreateApplyError(request.FixId, ErrorCodes.FixNotFound, "No matching code fix found for fixId.");
        }

        var updatedSolution = await operation.ApplyAsync(solution, ct).ConfigureAwait(false);
        var changedFiles = await CollectChangedFilesAsync(solution, updatedSolution, ct).ConfigureAwait(false);
        if (changedFiles.Count == 0)
        {
            return CreateApplyError(request.FixId, ErrorCodes.FixConflict, "Code fix could not produce any workspace changes.");
        }

        var (applied, applyError) = await _solutionAccessor.TryApplySolutionAsync(updatedSolution, ct).ConfigureAwait(false);
        if (!applied)
        {
            return new ApplyCodeFixResult(request.FixId, 0, Array.Empty<string>(),
                applyError ?? CreateError(ErrorCodes.InternalError, "Failed to apply code fix changes."));
        }

        var paths = changedFiles.Select(static file => file.FilePath).ToArray();
        return new ApplyCodeFixResult(request.FixId, paths.Length, paths);
    }

    public async Task<ExecuteCleanupResult> ExecuteCleanupAsync(ExecuteCleanupRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (!IsValidScope(request.Scope))
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                CreateError(ErrorCodes.InvalidRequest,
                    "scope must be one of: document, project, solution.",
                    ("operation", "execute_cleanup"),
                    ("field", "scope")));
        }

        if (string.Equals(request.Scope, "document", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(request.Path))
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is document.",
                    ("operation", "execute_cleanup"),
                    ("field", "path")));
        }

        if (string.Equals(request.Scope, "project", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(request.Path))
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is project.",
                    ("operation", "execute_cleanup"),
                    ("field", "path")));
        }

        var profile = string.IsNullOrWhiteSpace(request.PolicyProfile) ? "balanced" : request.PolicyProfile.Trim().ToLowerInvariant();
        if (!string.Equals(profile, "balanced", StringComparison.Ordinal))
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                CreateError(ErrorCodes.InvalidInput,
                    "policyProfile must be 'balanced' for cleanup.",
                    ("operation", "execute_cleanup"),
                    ("field", "policyProfile")));
        }

        var (solution, version, error) = await TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ExecuteCleanupResult(request.Scope, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), error);
        }

        var effectiveExpectedWorkspaceVersion = request.ExpectedWorkspaceVersion;

        var scopedDocuments = ResolveScopeDocuments(solution, request.Scope, request.Path)
            .OrderBy(static d => d.FilePath ?? d.Name, StringComparer.Ordinal)
            .ToArray();
        if (scopedDocuments.Length == 0)
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                CreateError(ErrorCodes.PathOutOfScope,
                    "The provided path is outside the selected solution scope.",
                    ("operation", "execute_cleanup"),
                    ("path", request.Path)));
        }

        const bool healthCheckPerformed = true;
        var autoReloadAttempted = false;
        var autoReloadSucceeded = false;
        var health = EvaluateWorkspaceFilesystemHealth(scopedDocuments);
        if (!health.IsConsistent)
        {
            if (_solutionAccessor is ISolutionSessionService sessionService)
            {
                autoReloadAttempted = true;
                var reload = await sessionService.ReloadSolutionAsync(new ReloadSolutionRequest(), ct).ConfigureAwait(false);
                autoReloadSucceeded = reload.Success;
                if (reload.Success)
                {
                    var (reloadedSolution, reloadedVersion, reloadError) = await TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
                    if (reloadedSolution == null)
                    {
                        return CreateStaleWorkspaceResult(
                            request.Scope,
                            healthCheckPerformed,
                            autoReloadAttempted,
                            autoReloadSucceeded,
                            health.MissingRootedFiles.Count,
                            reloadError?.Code);
                    }

                    solution = reloadedSolution;
                    version = reloadedVersion;
                    effectiveExpectedWorkspaceVersion = version;

                    scopedDocuments = ResolveScopeDocuments(solution, request.Scope, request.Path)
                        .OrderBy(static d => d.FilePath ?? d.Name, StringComparer.Ordinal)
                        .ToArray();
                    if (scopedDocuments.Length == 0)
                    {
                        return new ExecuteCleanupResult(
                            request.Scope,
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            BuildCleanupMetadataWarnings(healthCheckPerformed, autoReloadAttempted, autoReloadSucceeded),
                            CreateError(ErrorCodes.PathOutOfScope,
                                "The provided path is outside the selected solution scope.",
                                ("operation", "execute_cleanup"),
                                ("path", request.Path)));
                    }

                    health = EvaluateWorkspaceFilesystemHealth(scopedDocuments);
                }
                else
                {
                    return CreateStaleWorkspaceResult(
                        request.Scope,
                        healthCheckPerformed,
                        autoReloadAttempted,
                        autoReloadSucceeded,
                        health.MissingRootedFiles.Count,
                        reload.Error?.Code);
                }
            }

            if (!health.IsConsistent)
            {
                return CreateStaleWorkspaceResult(
                    request.Scope,
                    healthCheckPerformed,
                    autoReloadAttempted,
                    autoReloadSucceeded,
                    health.MissingRootedFiles.Count);
            }
        }

        var cleanupMetadataWarnings = BuildCleanupMetadataWarnings(healthCheckPerformed, autoReloadAttempted, autoReloadSucceeded);

        if (effectiveExpectedWorkspaceVersion.HasValue && effectiveExpectedWorkspaceVersion.Value != version)
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                cleanupMetadataWarnings,
                CreateError(ErrorCodes.WorkspaceChanged,
                    "Workspace changed before cleanup started.",
                    ("operation", "execute_cleanup")));
        }

        var updated = solution;
        updated = await ApplyDiagnosticCleanupStepAsync(updated, scopedDocuments, CleanupRemoveUnusedUsingDiagnostics, ct).ConfigureAwait(false);
        updated = await OrganizeUsingsAsync(updated, scopedDocuments, ct).ConfigureAwait(false);
        updated = await ApplyDiagnosticCleanupStepAsync(updated, scopedDocuments, CleanupModifierOrderDiagnostics, ct).ConfigureAwait(false);
        updated = await ApplyDiagnosticCleanupStepAsync(updated, scopedDocuments, CleanupReadonlyDiagnostics, ct).ConfigureAwait(false);
        updated = await FormatScopeAsync(updated, scopedDocuments, ct).ConfigureAwait(false);

        var changedFiles = await CollectChangedFilesAsync(solution, updated, ct).ConfigureAwait(false);
        var changedPaths = changedFiles.Select(static file => file.FilePath).ToArray();
        if (changedPaths.Length == 0)
        {
            return new ExecuteCleanupResult(
                request.Scope,
                BuildCleanupRuleIds(),
                Array.Empty<string>(),
                cleanupMetadataWarnings);
        }

        var (applyVersion, versionError) = await _solutionAccessor.GetWorkspaceVersionAsync(ct).ConfigureAwait(false);
        if (versionError != null)
        {
            return new ExecuteCleanupResult(request.Scope, BuildCleanupRuleIds(), Array.Empty<string>(), cleanupMetadataWarnings, versionError);
        }

        if (effectiveExpectedWorkspaceVersion.HasValue && effectiveExpectedWorkspaceVersion.Value != applyVersion)
        {
            return new ExecuteCleanupResult(
                request.Scope,
                BuildCleanupRuleIds(),
                Array.Empty<string>(),
                cleanupMetadataWarnings,
                CreateError(ErrorCodes.WorkspaceChanged,
                    "Workspace changed during cleanup execution.",
                    ("operation", "execute_cleanup")));
        }

        var (applied, applyError) = await _solutionAccessor.TryApplySolutionAsync(updated, ct).ConfigureAwait(false);
        if (!applied)
        {
            return new ExecuteCleanupResult(
                request.Scope,
                BuildCleanupRuleIds(),
                Array.Empty<string>(),
                cleanupMetadataWarnings,
                applyError ?? CreateError(ErrorCodes.InternalError, "Failed to apply cleanup changes.", ("operation", "execute_cleanup")));
        }

        return new ExecuteCleanupResult(request.Scope, BuildCleanupRuleIds(), changedPaths, cleanupMetadataWarnings);
    }

    private static ExecuteCleanupResult CreateStaleWorkspaceResult(
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
                ErrorCodes.StaleWorkspaceSnapshot,
                CleanupStaleWorkspaceMessage,
                ("operation", "execute_cleanup"),
                (CleanupHealthCheckPerformedDetail, BoolToLowerInvariantString(healthCheckPerformed)),
                (CleanupAutoReloadAttemptedDetail, BoolToLowerInvariantString(autoReloadAttempted)),
                (CleanupAutoReloadSucceededDetail, BoolToLowerInvariantString(autoReloadSucceeded)),
                (CleanupMissingFileCountDetail, missingFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (CleanupReloadErrorCodeDetail, reloadErrorCode)));
    }

    private static IReadOnlyList<string> BuildCleanupMetadataWarnings(bool healthCheckPerformed, bool autoReloadAttempted, bool autoReloadSucceeded)
        =>
        [
            $"meta.{CleanupHealthCheckPerformedDetail}={BoolToLowerInvariantString(healthCheckPerformed)}",
            $"meta.{CleanupAutoReloadAttemptedDetail}={BoolToLowerInvariantString(autoReloadAttempted)}",
            $"meta.{CleanupAutoReloadSucceededDetail}={BoolToLowerInvariantString(autoReloadSucceeded)}"
        ];

    private static CleanupWorkspaceHealth EvaluateWorkspaceFilesystemHealth(IReadOnlyList<Document> scopedDocuments)
    {
        var missingRootedFiles = scopedDocuments
            .Select(static document => document.FilePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .Where(static filePath => Path.IsPathRooted(filePath))
            .Where(static path => !File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CleanupWorkspaceHealth(missingRootedFiles.Length == 0, missingRootedFiles);
    }

    private static string BoolToLowerInvariantString(bool value)
        => value ? "true" : "false";

    private sealed record CleanupWorkspaceHealth(bool IsConsistent, IReadOnlyList<string> MissingRootedFiles);

    public async Task<RenameSymbolResult> RenameSymbolAsync(RenameSymbolRequest request, CancellationToken ct)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ct.ThrowIfCancellationRequested();

        var invalidInputError = TryCreateInvalidSymbolIdError(request.SymbolId, "rename-symbol");
        if (invalidInputError != null)
        {
            return CreateErrorResult(invalidInputError);
        }

        if (string.IsNullOrWhiteSpace(request.NewName))
        {
            return CreateErrorResult(ErrorCodes.InvalidNewName,
                "New name must be provided.",
                ("newName", request.NewName),
                ("operation", "rename-symbol"));
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return CreateErrorResult(error ??
                    new ErrorInfo(ErrorCodes.InternalError, "Unable to access the current solution."));
            }

            var symbol = await ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return CreateErrorResult(ErrorCodes.SymbolNotFound,
                    $"Symbol '{request.SymbolId}' could not be resolved.",
                    ("symbolId", request.SymbolId),
                    ("operation", "rename-symbol"));
            }

            if (!IsValidIdentifierForSymbol(symbol, request.NewName))
            {
                return CreateErrorResult(ErrorCodes.InvalidNewName,
                    $"'{request.NewName}' is not a valid identifier.",
                    ("newName", request.NewName),
                    ("operation", "rename-symbol"));
            }

            if (WouldConflict(symbol, request.NewName))
            {
                return CreateErrorResult(ErrorCodes.RenameConflict,
                    $"Renaming '{symbol.Name}' to '{request.NewName}' would conflict with an existing symbol.",
                    ("symbolId", request.SymbolId),
                    ("newName", request.NewName),
                    ("operation", "rename-symbol"));
            }

            var declarationKeys = GetSourceLocationKeys(symbol);
            var affectedLocations = await CollectAffectedLocationsAsync(symbol, solution, ct).ConfigureAwait(false);
            var renameOptions = new SymbolRenameOptions(RenameOverloads: false, RenameInStrings: false, RenameInComments: false, RenameFile: false);
            var renamedSolution = await Renamer.RenameSymbolAsync(solution, symbol, renameOptions, request.NewName, ct)
                .ConfigureAwait(false);
            var changes = renamedSolution.GetChanges(solution);
            var changedDocumentIds = changes.GetProjectChanges()
                .SelectMany(project => project.GetChangedDocuments())
                .Distinct()
                .ToList();
            var changedFiles = changedDocumentIds
                .Select(id => renamedSolution.GetDocument(id)?.FilePath ?? renamedSolution.GetDocument(id)?.Name ?? string.Empty)
                .Where(filePath => !string.IsNullOrEmpty(filePath))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            var renamedSymbol = await TryResolveRenamedSymbolAsync(renamedSolution, request.NewName, declarationKeys, ct)
                .ConfigureAwait(false);
            var renamedSymbolId = renamedSymbol != null ? SymbolIdentity.CreateId(renamedSymbol) : SymbolIdentity.CreateId(symbol);

            var (applied, applyError) = await _solutionAccessor.TryApplySolutionAsync(renamedSolution, ct).ConfigureAwait(false);
            if (!applied)
            {
                return CreateErrorResult(applyError ??
                    CreateError(ErrorCodes.InternalError,
                        "Failed to update the active solution after rename.",
                        ("symbolId", request.SymbolId),
                        ("newName", request.NewName),
                        ("operation", "rename-symbol")));
            }

            return new RenameSymbolResult(
                renamedSymbolId,
                changedDocumentIds.Count,
                affectedLocations,
                changedFiles);
        }
        catch (ArgumentException ex)
        {
            return CreateErrorResult(ErrorCodes.InvalidNewName,
                $"'{request.NewName}' is invalid: {ex.Message}",
                ("newName", request.NewName),
                ("operation", "rename-symbol"));
        }
        catch (InvalidOperationException ex)
        {
            return CreateErrorResult(ErrorCodes.RenameConflict,
                $"Rename conflict: {ex.Message}",
                ("symbolId", request.SymbolId),
                ("newName", request.NewName),
                ("operation", "rename-symbol"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RenameSymbol failed for {SymbolId}", request.SymbolId);
            return CreateErrorResult(ErrorCodes.InternalError,
                $"Failed to rename symbol '{request.SymbolId}': {ex.Message}",
                ("symbolId", request.SymbolId),
                ("newName", request.NewName),
                ("operation", "rename-symbol"));
        }
    }

    private static RenameSymbolResult CreateErrorResult(string code, string message, params (string Key, string? Value)[] details)
        => new(null, 0, Array.Empty<SourceLocation>(), Array.Empty<string>(), CreateError(code, message, details));

    private static IReadOnlyList<string> BuildCleanupRuleIds()
        =>
        [
            CleanupRuleRemoveUnusedUsings,
            CleanupRuleOrganizeUsings,
            CleanupRuleFixModifierOrder,
            CleanupRuleAddReadonly,
            CleanupRuleFormat
        ];

    private async Task<Solution> ApplyDiagnosticCleanupStepAsync(
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

    private async Task<Solution> OrganizeUsingsAsync(Solution solution, IReadOnlyList<Document> scopeDocuments, CancellationToken ct)
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

            var organizedRoot = OrganizeUsings(compilationUnit);
            if (organizedRoot.IsEquivalentTo(compilationUnit))
            {
                continue;
            }

            updated = updated.WithDocumentSyntaxRoot(document.Id, organizedRoot);
        }

        return updated;
    }

    private static CompilationUnitSyntax OrganizeUsings(CompilationUnitSyntax root)
    {
        var updated = root.WithUsings(SortUsingDirectives(root.Usings));
        foreach (var namespaceDeclaration in updated.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            var orderedUsings = SortUsingDirectives(namespaceDeclaration.Usings);
            if (orderedUsings == namespaceDeclaration.Usings)
            {
                continue;
            }

            updated = updated.ReplaceNode(namespaceDeclaration, namespaceDeclaration.WithUsings(orderedUsings));
        }

        return updated;
    }

    private static SyntaxList<UsingDirectiveSyntax> SortUsingDirectives(SyntaxList<UsingDirectiveSyntax> usings)
    {
        if (usings.Count <= 1)
        {
            return usings;
        }

        return SyntaxFactory.List(
            usings
                .OrderBy(static directive => directive.Alias == null ? 1 : 0)
                .ThenBy(static directive => directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? 1 : 0)
                .ThenBy(static directive => directive.Name?.ToString(), StringComparer.Ordinal)
                .ThenBy(static directive => directive.Alias?.Name.Identifier.ValueText ?? string.Empty, StringComparer.Ordinal));
    }

    private async Task<Solution> FormatScopeAsync(Solution solution, IReadOnlyList<Document> scopeDocuments, CancellationToken ct)
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

    private static RenameSymbolResult CreateErrorResult(ErrorInfo? error)
    {
        var safeError = error ?? new ErrorInfo(ErrorCodes.InternalError, "An unknown error occurred while renaming a symbol.");
        return new RenameSymbolResult(null, 0, Array.Empty<SourceLocation>(), Array.Empty<string>(), safeError);
    }

    private static ErrorInfo CreateError(string code, string message, params (string Key, string? Value)[] details)
    {
        if (details.Length == 0)
        {
            return new ErrorInfo(code, message);
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in details)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                map[key] = value;
            }
        }

        return map.Count == 0 ? new ErrorInfo(code, message) : new ErrorInfo(code, message, map);
    }

    private static ErrorInfo? TryCreateInvalidSymbolIdError(string symbolId, string operation)
    {
        if (!string.IsNullOrWhiteSpace(symbolId))
        {
            return null;
        }

        return CreateError(
            ErrorCodes.InvalidInput,
            "symbolId must be a non-empty, non-whitespace string.",
            ("parameter", "symbolId"),
            ("operation", operation));
    }

    private async Task<(Solution? Solution, ErrorInfo? Error)> TryGetSolutionAsync(CancellationToken ct)
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

    private static bool IsValidIdentifierForSymbol(ISymbol symbol, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (symbol.Language == LanguageNames.CSharp)
        {
            return SyntaxFacts.IsValidIdentifier(candidate);
        }

        return true;
    }

    private static SourceLocation CreateSourceLocation(Location location)
    {
        var span = location.GetLineSpan();
        var filePath = span.Path ?? string.Empty;
        var start = span.StartLinePosition;
        return new SourceLocation(filePath, start.Line + 1, start.Character + 1);
    }

    private static string GetLocationKey(SourceLocation location)
        => string.Join(':', location.FilePath, location.Line, location.Column);

    private static async Task<IReadOnlyList<SourceLocation>> CollectAffectedLocationsAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var locations = new List<SourceLocation>();

        foreach (var reference in references)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var location in reference.Locations)
            {
                if (!location.Location.IsInSource)
                {
                    continue;
                }

                var sourceLocation = CreateSourceLocation(location.Location);
                var key = GetLocationKey(sourceLocation);
                if (seen.Add(key))
                {
                    locations.Add(sourceLocation);
                }
            }
        }

        foreach (var location in symbol.Locations.Where(l => l.IsInSource))
        {
            var sourceLocation = CreateSourceLocation(location);
            var key = GetLocationKey(sourceLocation);
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

    private async Task<ISymbol?> ResolveSymbolAsync(string symbolId, Solution solution, CancellationToken ct)
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

            var resolved = SymbolIdentity.Resolve(symbolId, compilation, ct);
            if (resolved != null)
            {
                return resolved.OriginalDefinition ?? resolved;
            }
        }

        return null;
    }

    private static ISet<string> GetSourceLocationKeys(ISymbol symbol)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in symbol.Locations.Where(static l => l.IsInSource))
        {
            var sourceLocation = CreateSourceLocation(location);
            keys.Add(GetLocationKey(sourceLocation));
        }

        return keys;
    }

    private async Task<ISymbol?> TryResolveRenamedSymbolAsync(Solution solution,
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
                             .Select(CreateSourceLocation))
                {
                    if (originalDeclarationKeys.Contains(GetLocationKey(sourceLocation)))
                    {
                        return normalizedCandidate;
                    }
                }
            }
        }

        return null;
    }

    private static bool WouldConflict(ISymbol symbol, string newName)
    {
        if (string.Equals(symbol.Name, newName, StringComparison.Ordinal))
        {
            return false;
        }

        var members = symbol.ContainingType?.GetMembers(newName) ?? default;
        if (members.IsDefaultOrEmpty && symbol.ContainingNamespace != null)
        {
            members = symbol.ContainingNamespace.GetMembers(newName)
                .Cast<ISymbol>()
                .ToImmutableArray();
        }

        if (members.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var member in members)
        {
            if (SymbolConflicts(symbol, member))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SymbolConflicts(ISymbol original, ISymbol existing)
    {
        var normalizedOriginal = original.OriginalDefinition ?? original;
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
            {
                return false;
            }

            for (var i = 0; i < originalMethod.Parameters.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(originalMethod.Parameters[i].Type, existingMethod.Parameters[i].Type))
                {
                    return false;
                }
            }

            return true;
        }

        if (normalizedOriginal is IPropertySymbol && normalizedExisting is IPropertySymbol)
        {
            return true;
        }

        if (normalizedOriginal is IFieldSymbol && normalizedExisting is IFieldSymbol)
        {
            return true;
        }

        if (normalizedOriginal is IEventSymbol && normalizedExisting is IEventSymbol)
        {
            return true;
        }

        if (normalizedOriginal is INamedTypeSymbol && normalizedExisting is INamedTypeSymbol)
        {
            return true;
        }

        return true;
    }

    private static bool IsValidScope(string scope)
        => string.Equals(scope, "document", StringComparison.Ordinal)
           || string.Equals(scope, "project", StringComparison.Ordinal)
           || string.Equals(scope, "solution", StringComparison.Ordinal);

    private async Task<(Solution? Solution, int Version, ErrorInfo? Error)> TryGetSolutionWithVersionAsync(CancellationToken ct)
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

    private static IEnumerable<Document> ResolveScopeDocuments(Solution solution, string scope, string? path)
    {
        if (string.Equals(scope, "solution", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(path))
        {
            return solution.Projects.SelectMany(static project => project.Documents);
        }

        if (string.Equals(scope, "project", StringComparison.Ordinal))
        {
            return solution.Projects
                .Where(project => MatchesByNormalizedPath(project.FilePath, path)
                                  || string.Equals(project.Name, path, StringComparison.OrdinalIgnoreCase))
                .SelectMany(static project => project.Documents);
        }

        return solution.Projects
            .SelectMany(static project => project.Documents)
            .Where(document => MatchesByNormalizedPath(document.FilePath, path));
    }

    private static bool MatchesByNormalizedPath(string? candidatePath, string path)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

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

    private static HashSet<string>? CreateDiagnosticFilter(IReadOnlyList<string>? diagnosticIds)
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

    private static bool IsSupportedDiagnostic(Diagnostic diagnostic)
        => SupportedFixDiagnosticIds.Contains(diagnostic.Id);

    private static async Task<LocalDeclarationStatementSyntax?> TryGetUnusedLocalDeclarationAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root == null)
        {
            return null;
        }

        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        var declaration = token.Parent?.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (declaration == null)
        {
            return null;
        }

        if (declaration.Declaration.Variables.Count != 1)
        {
            return null;
        }

        return declaration;
    }

    private static CodeFixDescriptor CreateFixDescriptor(
        Document document,
        Diagnostic diagnostic,
        LocalDeclarationStatementSyntax declaration,
        int workspaceVersion)
    {
        var location = CreateSourceLocation(diagnostic.Location);
        var variableName = declaration.Declaration.Variables[0].Identifier.ValueText;
        var title = $"Remove unused local variable '{variableName}'";
        var filePath = document.FilePath ?? document.Name;
        var fixId = BuildFixId(workspaceVersion, diagnostic.Id, declaration.Span.Start, declaration.Span.Length, filePath);
        return new CodeFixDescriptor(fixId, title, diagnostic.Id, SupportedFixCategory, location, filePath);
    }

    private static string BuildFixId(int workspaceVersion, string diagnosticId, int spanStart, int spanLength, string filePath)
    {
        var encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(filePath));
        return string.Join('|', "v1", workspaceVersion, SupportedFixOperation, diagnosticId, spanStart, spanLength, encodedPath);
    }

    private static ParsedFixId? ParseFixId(string fixId)
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
            || !string.Equals(parts[2], SupportedFixOperation, StringComparison.Ordinal)
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

    private async Task<FixOperation?> TryBuildFixOperationAsync(Solution solution, ParsedFixId fix, CancellationToken ct)
    {
        if (!SupportedFixDiagnosticIds.Contains(fix.DiagnosticId))
        {
            return null;
        }

        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => MatchesByNormalizedPath(d.FilePath, fix.FilePath));
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

    private static async Task<IReadOnlyList<ChangedFilePreview>> CollectChangedFilesAsync(Solution original, Solution updated, CancellationToken ct)
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

    private async Task<IReadOnlyList<DiscoveredAction>> DiscoverActionsAtPositionAsync(
        Document document,
        int position,
        int? selectionStart,
        int? selectionLength,
        CancellationToken ct)
    {
        var discovered = new List<DiscoveredAction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var filePath = document.FilePath ?? document.Name;
        var selectionSpan = CreateSelectionSpan(position, selectionStart, selectionLength);

        var providerDiagnostics = await GetProviderDiagnosticsForDocumentAsync(document, ct).ConfigureAwait(false);
        foreach (var diagnostic in providerDiagnostics)
        {
            ct.ThrowIfCancellationRequested();
            if (!diagnostic.Location.IsInSource)
            {
                continue;
            }

            var span = diagnostic.Location.SourceSpan;
            if (!span.Contains(position) || !IntersectsSelection(span, selectionStart, selectionLength))
            {
                continue;
            }

            var fixes = await CollectCodeFixActionsAsync(document, diagnostic, ct).ConfigureAwait(false);
            foreach (var fix in fixes)
            {
                var providerKey = BuildProviderCodeFixKey(fix.ProviderTypeName, diagnostic.Id, fix.Action.EquivalenceKey, fix.Action.Title);
                var category = GetCodeFixCategory(diagnostic);
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
                    CreateSourceLocation(diagnostic.Location),
                    diagnostic.Id,
                    NormalizeNullable(fix.Action.EquivalenceKey)));
            }
        }

        foreach (var action in await CollectCodeRefactoringActionsAsync(document, selectionSpan, ct).ConfigureAwait(false))
        {
            var span = selectionSpan;
            var providerKey = BuildProviderRefactoringKey(action.ProviderTypeName, action.Action.EquivalenceKey, action.Action.Title);
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
                await CreateSourceLocationFromSpanAsync(document, span, ct).ConfigureAwait(false),
                null,
                NormalizeNullable(action.Action.EquivalenceKey)));
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
                if (!IsSupportedDiagnostic(diagnostic))
                {
                    continue;
                }

                var declaration = await TryGetUnusedLocalDeclarationAsync(document, diagnostic, ct).ConfigureAwait(false);
                if (declaration == null)
                {
                    continue;
                }

                var span = declaration.Span;
                if (!span.Contains(position) || !IntersectsSelection(span, selectionStart, selectionLength))
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
                    CreateSourceLocation(diagnostic.Location),
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
            if (IntersectsSelection(typeSpan, selectionStart, selectionLength))
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
                        CreateSourceLocation(localDeclaration.GetLocation()),
                        null,
                        RefactoringOperationUseVar));
                }
            }
        }

        return discovered;
    }

    private async Task<FixOperation?> TryBuildActionOperationAsync(Solution solution, ActionExecutionIdentity identity, CancellationToken ct)
    {
        if (TryParseProviderCodeFixKey(identity.ProviderActionKey, out var codeFixKey))
        {
            return await TryBuildProviderCodeFixOperationAsync(solution, identity, codeFixKey, ct).ConfigureAwait(false);
        }

        if (TryParseProviderRefactoringKey(identity.ProviderActionKey, out var refactoringKey))
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

    private async Task<FixOperation?> TryBuildUseVarOperationAsync(Solution solution, ActionExecutionIdentity identity, CancellationToken ct)
    {
        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => MatchesByNormalizedPath(d.FilePath, identity.FilePath));
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

    private async Task<FixOperation?> TryBuildProviderCodeFixOperationAsync(Solution solution,
        ActionExecutionIdentity identity,
        ProviderCodeFixKey key,
        CancellationToken ct)
    {
        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => MatchesByNormalizedPath(d.FilePath, identity.FilePath));
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
                .FirstOrDefault(action => MatchesProviderAction(identity, action, key.ActionTitle));
            if (selected == null)
            {
                continue;
            }

            return new FixOperation(
                selected.Title,
                async (currentSolution, cancellationToken) =>
                {
                    var currentDocument = FindDocument(currentSolution, identity.FilePath);
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
                        .FirstOrDefault(action => MatchesProviderAction(identity, action, key.ActionTitle));

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

    private async Task<FixOperation?> TryBuildProviderRefactoringOperationAsync(Solution solution,
        ActionExecutionIdentity identity,
        ProviderRefactoringKey key,
        CancellationToken ct)
    {
        var document = FindDocument(solution, identity.FilePath);
        if (document == null)
        {
            return null;
        }

        var span = new TextSpan(identity.SpanStart, identity.SpanLength);
        var actions = await CollectCodeRefactoringActionsAsync(document, span, ct).ConfigureAwait(false);
        var selected = actions
            .Where(candidate => string.Equals(candidate.ProviderTypeName, key.ProviderTypeName, StringComparison.Ordinal))
            .Select(candidate => candidate.Action)
            .FirstOrDefault(action => MatchesProviderAction(identity, action, key.ActionTitle));
        if (selected == null)
        {
            return null;
        }

        return new FixOperation(
            selected.Title,
            async (currentSolution, cancellationToken) =>
            {
                var currentDocument = FindDocument(currentSolution, identity.FilePath);
                if (currentDocument == null)
                {
                    return currentSolution;
                }

                var currentActions = await CollectCodeRefactoringActionsAsync(currentDocument, span, cancellationToken).ConfigureAwait(false);
                var currentAction = currentActions
                    .Where(candidate => string.Equals(candidate.ProviderTypeName, key.ProviderTypeName, StringComparison.Ordinal))
                    .Select(candidate => candidate.Action)
                    .FirstOrDefault(action => MatchesProviderAction(identity, action, key.ActionTitle));
                if (currentAction == null)
                {
                    return currentSolution;
                }

                var applied = await TryApplyCodeActionToSolutionAsync(currentSolution, currentAction, cancellationToken).ConfigureAwait(false);
                return applied ?? currentSolution;
            });
    }

    private static Document? FindDocument(Solution solution, string filePath)
        => solution.Projects.SelectMany(static project => project.Documents)
            .FirstOrDefault(d => MatchesByNormalizedPath(d.FilePath, filePath));

    private static bool MatchesProviderAction(ActionExecutionIdentity identity, CodeAction action, string actionTitle)
    {
        if (!string.IsNullOrWhiteSpace(identity.RefactoringId)
            && string.Equals(identity.RefactoringId, action.EquivalenceKey, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(action.Title, actionTitle, StringComparison.Ordinal);
    }

    private async Task<IReadOnlyList<Diagnostic>> GetProviderDiagnosticsForDocumentAsync(Document document, CancellationToken ct)
    {
        var diagnostics = new List<Diagnostic>();
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel != null)
        {
            diagnostics.AddRange(semanticModel.GetDiagnostics()
                .Where(static d => d.Location.IsInSource));
        }

        var catalog = s_providerCatalog.Value;
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

    private async Task<IReadOnlyList<ProviderCodeActionCandidate>> CollectCodeFixActionsAsync(Document document, Diagnostic diagnostic, CancellationToken ct)
    {
        var catalog = s_providerCatalog.Value;
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

    private async Task<IReadOnlyList<ProviderCodeActionCandidate>> CollectCodeRefactoringActionsAsync(Document document, TextSpan selectionSpan, CancellationToken ct)
    {
        var catalog = s_providerCatalog.Value;
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

    private static TextSpan CreateSelectionSpan(int position, int? selectionStart, int? selectionLength)
    {
        if (selectionStart.HasValue && selectionLength.HasValue)
        {
            return new TextSpan(selectionStart.Value, selectionLength.Value);
        }

        return new TextSpan(position, 0);
    }

    private static string GetCodeFixCategory(Diagnostic diagnostic)
        => string.IsNullOrWhiteSpace(diagnostic.Descriptor.Category)
            ? SupportedFixCategory
            : diagnostic.Descriptor.Category.Trim().ToLowerInvariant();

    private static string BuildProviderCodeFixKey(string providerType, string diagnosticId, string? equivalenceKey, string title)
        => string.Join('|', "cf", EncodeKey(providerType), EncodeKey(diagnosticId), EncodeKey(equivalenceKey), EncodeKey(title));

    private static string BuildProviderRefactoringKey(string providerType, string? equivalenceKey, string title)
        => string.Join('|', "rf", EncodeKey(providerType), EncodeKey(equivalenceKey), EncodeKey(title));

    private static bool TryParseProviderCodeFixKey(string key, out ProviderCodeFixKey parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split('|');
        if (parts.Length != 5 || !string.Equals(parts[0], "cf", StringComparison.Ordinal))
        {
            return false;
        }

        var providerType = DecodeKey(parts[1]);
        var diagnosticId = DecodeKey(parts[2]);
        if (string.IsNullOrWhiteSpace(providerType) || string.IsNullOrWhiteSpace(diagnosticId))
        {
            return false;
        }

        parsed = new ProviderCodeFixKey(providerType, diagnosticId, NormalizeNullable(DecodeKey(parts[3])), DecodeKey(parts[4]));
        return true;
    }

    private static bool TryParseProviderRefactoringKey(string key, out ProviderRefactoringKey parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split('|');
        if (parts.Length != 4 || !string.Equals(parts[0], "rf", StringComparison.Ordinal))
        {
            return false;
        }

        var providerType = DecodeKey(parts[1]);
        if (string.IsNullOrWhiteSpace(providerType))
        {
            return false;
        }

        parsed = new ProviderRefactoringKey(providerType, NormalizeNullable(DecodeKey(parts[2])), DecodeKey(parts[3]));
        return true;
    }

    private static string EncodeKey(string? value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string DecodeKey(string encoded)
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

    private static async Task<SourceLocation> CreateSourceLocationFromSpanAsync(Document document, TextSpan span, CancellationToken ct)
    {
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var line = text.Lines.GetLineFromPosition(span.Start);
        return new SourceLocation(document.FilePath ?? document.Name, line.LineNumber + 1, span.Start - line.Start + 1);
    }

    private async Task<Solution?> TryApplyCodeActionToSolutionAsync(Solution currentSolution, CodeAction action, CancellationToken ct)
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

    private void LogActionPipelineFlow(
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

    private static (ImmutableArray<DiagnosticAnalyzer> Analyzers,
        ImmutableArray<CodeFixProvider> CodeFixProviders,
        ImmutableArray<CodeRefactoringProvider> RefactoringProviders,
        Exception? Error) LoadProviderCatalog()
    {
        try
        {
            var analyzerPath = ResolveRoslynatorAssemblyPath(
                RoslynatorAnalyzerPackageId,
                RoslynatorAnalyzerPackageVersion,
                null,
                RoslynatorAnalyzerRelativePathSegments,
                RoslynatorAnalyzerFilename);
            var codeFixPath = ResolveRoslynatorAssemblyPath(
                RoslynatorAnalyzerPackageId,
                RoslynatorAnalyzerPackageVersion,
                RoslynatorCodeFixPathEnvVar,
                RoslynatorCodeFixRelativePathSegments,
                RoslynatorCodeFixesFilename);

            var loader = new RoslynatorProviderLoader();
            loader.AddDependencyLocation(analyzerPath);
            loader.AddDependencyLocation(codeFixPath);

            var analyzerReference = new AnalyzerFileReference(analyzerPath, loader);
            var analyzers = analyzerReference.GetAnalyzers(LanguageNames.CSharp);

            var codeFixes = LoadCodeFixProviders(codeFixPath);

            var refactorings = LoadRefactoringProviders(codeFixPath);
            return (analyzers, codeFixes, refactorings, null);
        }
        catch (Exception ex)
        {
            return (ImmutableArray<DiagnosticAnalyzer>.Empty,
                ImmutableArray<CodeFixProvider>.Empty,
                ImmutableArray<CodeRefactoringProvider>.Empty,
                ex);
        }
    }

    private static ImmutableArray<CodeRefactoringProvider> LoadRefactoringProviders(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var providers = assembly.GetTypes()
            .Where(type => typeof(CodeRefactoringProvider).IsAssignableFrom(type)
                           && !type.IsAbstract)
            .Select(CreateProviderInstance<CodeRefactoringProvider>)
            .Where(static provider => provider != null)
            .Cast<CodeRefactoringProvider>()
            .ToImmutableArray();
        return providers;
    }

    private static ImmutableArray<CodeFixProvider> LoadCodeFixProviders(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        return assembly.GetTypes()
            .Where(type => typeof(CodeFixProvider).IsAssignableFrom(type)
                           && !type.IsAbstract)
            .Select(CreateProviderInstance<CodeFixProvider>)
            .Where(static provider => provider != null)
            .Cast<CodeFixProvider>()
            .ToImmutableArray();
    }

    private static TProvider? CreateProviderInstance<TProvider>(Type providerType)
        where TProvider : class
    {
        var instanceProperty = providerType.GetProperty(
            "Instance",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (instanceProperty?.GetValue(null) is TProvider shared)
        {
            return shared;
        }

        try
        {
            return Activator.CreateInstance(providerType, nonPublic: true) as TProvider;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveRoslynatorAssemblyPath(
        string packageId,
        string packageVersion,
        string? overrideEnvVar,
        IReadOnlyList<string> relativeSegments,
        string filename)
    {
        if (!string.IsNullOrWhiteSpace(overrideEnvVar))
        {
            var overridePath = Environment.GetEnvironmentVariable(overrideEnvVar);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                if (File.Exists(overridePath))
                {
                    return overridePath;
                }

                throw new InvalidOperationException(
                    $"Roslynator assembly path '{overridePath}' configured via '{overrideEnvVar}' could not be found.");
            }
        }

        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        var packageDirectory = Path.Combine(packagesRoot, packageId, packageVersion);
        if (!Directory.Exists(packageDirectory))
        {
            throw new InvalidOperationException(
                $"NuGet package '{packageId}' version '{packageVersion}' was not found under '{packagesRoot}'.");
        }

        var candidate = Path.Combine(packageDirectory, Path.Combine(relativeSegments.ToArray()));
        if (!File.Exists(candidate))
        {
            throw new InvalidOperationException($"Unable to locate '{filename}' under '{packageDirectory}'.");
        }

        return candidate;
    }

    private sealed class RoslynatorProviderLoader : IAnalyzerAssemblyLoader
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Assembly> _assemblies = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public RoslynatorProviderLoader()
        {
            AssemblyLoadContext.Default.Resolving += ResolveAssembly;
        }

        public void AddDependencyLocation(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            LoadFromPath(fullPath);
        }

        public Assembly LoadFromPath(string fullPath)
        {
            lock (_gate)
            {
                if (_assemblies.TryGetValue(fullPath, out var existing))
                {
                    return existing;
                }

                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                _assemblies[fullPath] = assembly;
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    _directories.Add(directory);
                }

                return assembly;
            }
        }

        private Assembly? ResolveAssembly(AssemblyLoadContext _, AssemblyName name)
        {
            lock (_gate)
            {
                foreach (var directory in _directories)
                {
                    var path = Path.Combine(directory, name.Name + ".dll");
                    if (_assemblies.TryGetValue(path, out var existing))
                    {
                        return existing;
                    }

                    if (File.Exists(path))
                    {
                        return LoadFromPath(path);
                    }
                }
            }

            return null;
        }
    }

    private static bool IntersectsSelection(TextSpan span, int? selectionStart, int? selectionLength)
    {
        if (!selectionStart.HasValue || !selectionLength.HasValue)
        {
            return true;
        }

        var selection = new TextSpan(selectionStart.Value, selectionLength.Value);
        return selection.OverlapsWith(span) || selection.Contains(span.Start) || span.Contains(selection.Start);
    }

    private static PreviewCodeFixResult CreatePreviewError(string code, string message)
        => CreatePreviewError(CreateError(code, message, ("operation", "preview_code_fix")));

    private static PreviewCodeFixResult CreatePreviewError(ErrorInfo error)
        => new(string.Empty, string.Empty, Array.Empty<ChangedFilePreview>(), error);

    private static ApplyCodeFixResult CreateApplyError(string fixId, string code, string message)
        => new(fixId,
            0,
            Array.Empty<string>(),
            CreateError(code, message, ("fixId", fixId), ("operation", "apply_code_fix")));

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private readonly record struct ProviderCodeFixKey(string ProviderTypeName, string DiagnosticId, string? EquivalenceKey, string ActionTitle);

    private readonly record struct ProviderRefactoringKey(string ProviderTypeName, string? EquivalenceKey, string ActionTitle);

    private sealed record ProviderCodeActionCandidate(string ProviderTypeName, CodeAction Action);

    private sealed record DiscoveredAction(
        string Title,
        string Category,
        string Origin,
        string ProviderActionKey,
        string FilePath,
        int SpanStart,
        int SpanLength,
        SourceLocation Location,
        string? DiagnosticId,
        string? RefactoringId);

    private sealed record ActionExecutionIdentity(
        int WorkspaceVersion,
        string PolicyProfile,
        string Origin,
        string Category,
        string ProviderActionKey,
        string FilePath,
        int SpanStart,
        int SpanLength,
        string? DiagnosticId,
        string? RefactoringId,
        SourceLocation Location)
    {
        public DiscoveredAction ToDiscoveredAction()
            => new(
                string.Empty,
                Category,
                Origin,
                ProviderActionKey,
                FilePath,
                SpanStart,
                SpanLength,
                Location,
                DiagnosticId,
                RefactoringId);
    }

    private sealed class ActionIdentityService
    {
        public string Create(int workspaceVersion, string policyProfile, DiscoveredAction action)
            => string.Join('|',
                "v1",
                workspaceVersion,
                Encode(policyProfile),
                Encode(action.Origin),
                Encode(action.Category),
                Encode(action.ProviderActionKey),
                action.SpanStart,
                action.SpanLength,
                Encode(action.FilePath),
                Encode(action.DiagnosticId),
                Encode(action.RefactoringId),
                action.Location.Line,
                action.Location.Column);

        public ActionExecutionIdentity? Parse(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return null;
            }

            var parts = actionId.Split('|');
            if (parts.Length != 13 || !string.Equals(parts[0], "v1", StringComparison.Ordinal))
            {
                return null;
            }

            if (!int.TryParse(parts[1], out var workspaceVersion)
                || !int.TryParse(parts[6], out var spanStart)
                || !int.TryParse(parts[7], out var spanLength)
                || !int.TryParse(parts[11], out var line)
                || !int.TryParse(parts[12], out var column))
            {
                return null;
            }

            var policyProfile = Decode(parts[2]);
            var origin = Decode(parts[3]);
            var category = Decode(parts[4]);
            var providerKey = Decode(parts[5]);
            var filePath = Decode(parts[8]);
            if (string.IsNullOrWhiteSpace(origin)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(providerKey)
                || string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            return new ActionExecutionIdentity(
                workspaceVersion,
                string.IsNullOrWhiteSpace(policyProfile) ? PolicyProfileDefault : policyProfile,
                origin,
                category,
                providerKey,
                filePath,
                spanStart,
                spanLength,
                NormalizeNullable(Decode(parts[9])),
                NormalizeNullable(Decode(parts[10])),
                new SourceLocation(filePath, line, column));
        }

        private static string Encode(string? value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

        private static string Decode(string encoded)
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

        private static string? NormalizeNullable(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class RefactoringPolicyService
    {
        public PolicyAssessment Evaluate(DiscoveredAction action, string policyProfile)
        {
            var profile = string.IsNullOrWhiteSpace(policyProfile)
                ? PolicyProfileDefault
                : policyProfile.Trim();

            if (!string.Equals(profile, PolicyProfileDefault, StringComparison.OrdinalIgnoreCase))
            {
                return new PolicyAssessment(
                    "block",
                    "blocked",
                    "unknown_profile",
                    $"Policy profile '{profile}' is not supported.");
            }

            if (string.Equals(action.ProviderActionKey, RefactoringOperationUseVar, StringComparison.Ordinal))
            {
                return new PolicyAssessment(
                    "review_required",
                    "review_required",
                    "manual_review_required",
                    "This refactoring requires manual review before apply.");
            }

            if (string.Equals(action.Origin, OriginRoslynatorCodeFix, StringComparison.Ordinal)
                && string.Equals(action.Category, SupportedFixCategory, StringComparison.Ordinal)
                && action.DiagnosticId != null
                && SupportedFixDiagnosticIds.Contains(action.DiagnosticId))
            {
                return new PolicyAssessment(
                    "allow",
                    "safe",
                    "allowlisted",
                    "Action is allowlisted in the default policy profile.");
            }

            return new PolicyAssessment(
                "block",
                "blocked",
                "not_allowlisted",
                "Action is not allowlisted in the default policy profile.");
        }
    }

    private sealed record PolicyAssessment(string Decision, string RiskLevel, string ReasonCode, string ReasonMessage);

    private sealed record ParsedFixId(int WorkspaceVersion, string DiagnosticId, int SpanStart, int SpanLength, string FilePath);

    private sealed record FixOperation(string Title, Func<Solution, CancellationToken, Task<Solution>> ApplyAsync);

    private static class SymbolIdentity
    {
        private static readonly MethodInfo s_createString;
        private static readonly MethodInfo s_resolveString;
        private static readonly PropertyInfo s_resolutionSymbol;

        static SymbolIdentity()
        {
            var assembly = typeof(SymbolFinder).Assembly;
            var symbolKeyType = assembly.GetType("Microsoft.CodeAnalysis.SymbolKey", throwOnError: true)!;
            var resolutionType = assembly.GetType("Microsoft.CodeAnalysis.SymbolKeyResolution", throwOnError: true)!;

            s_createString = symbolKeyType.GetMethod("CreateString", BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(ISymbol), typeof(CancellationToken) },
                modifiers: null)
                ?? throw new InvalidOperationException("Unable to locate SymbolKey.CreateString");

            s_resolveString = symbolKeyType.GetMethod("ResolveString", BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(Compilation), typeof(bool), typeof(CancellationToken) },
                modifiers: null)
                ?? throw new InvalidOperationException("Unable to locate SymbolKey.ResolveString");

            s_resolutionSymbol = resolutionType.GetProperty("Symbol", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Unable to locate SymbolKeyResolution.Symbol");
        }

        public static string CreateId(ISymbol symbol)
        {
            var resolved = symbol.OriginalDefinition ?? symbol;
            var result = (string?)s_createString.Invoke(null, new object?[] { resolved, CancellationToken.None });
            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }

            return resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        public static ISymbol? Resolve(string identifier, Compilation compilation, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return null;
            }

            var resolution = s_resolveString.Invoke(null, new object?[] { identifier, compilation, true, ct });
            if (resolution == null)
            {
                return null;
            }

            return (ISymbol?)s_resolutionSymbol.GetValue(resolution);
        }
    }
}
