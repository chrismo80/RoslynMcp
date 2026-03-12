using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Agent;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace RoslynMcp.Infrastructure.Refactoring;

/// <summary>
/// Executes code fixes and refactorings at text positions.
/// Supports Roslynator analyzers, code fixes, and code refactorings.
/// </summary>
internal sealed class RefactoringActionOperations(RefactoringOperationOrchestrator owner)
{
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

        var requestValidationError = request.ValidateGetRefactoringsAtPosition();
        if (requestValidationError != null)
        {
            owner.LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.InvalidInput, affectedDocumentCount);
            return requestValidationError;
        }

        var (solution, workspaceVersion, error) = await owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            successCode = error?.Code ?? ErrorCodes.InternalError;
            owner.LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, successCode, affectedDocumentCount);
            return new GetRefactoringsAtPositionResult(Array.Empty<RefactoringActionDescriptor>(), error);
        }

        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => d.FilePath.MatchesByNormalizedPath(request.Path));
        if (document == null)
        {
            var pathError = new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.PathOutOfScope,
                    "The provided path is outside the selected solution scope.",
                    ("path", request.Path),
                    ("operation", "get_refactorings_at_position")));
            owner.LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.PathOutOfScope, affectedDocumentCount);
            return pathError;
        }

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        if (request.Line > text.Lines.Count)
        {
            var invalidLine = new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.InvalidInput,
                    "line is outside document bounds.",
                    ("operation", "get_refactorings_at_position")));
            owner.LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.InvalidInput, affectedDocumentCount);
            return invalidLine;
        }

        var line = text.Lines[request.Line - 1];
        var maxColumn = line.Span.Length + 1;
        if (request.Column > maxColumn)
        {
            var invalidColumn = new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.InvalidInput,
                    "column is outside line bounds.",
                    ("operation", "get_refactorings_at_position")));
            owner.LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.InvalidInput, affectedDocumentCount);
            return invalidColumn;
        }

        var selectionStart = request.SelectionStart;
        var selectionLength = request.SelectionLength;
        if (selectionStart.HasValue && selectionLength.HasValue && selectionStart.Value + selectionLength.Value > text.Length)
        {
            var invalidRange = new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.InvalidInput,
                    "selection is outside document bounds.",
                    ("operation", "get_refactorings_at_position")));
            owner.LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, ErrorCodes.InvalidInput, affectedDocumentCount);
            return invalidRange;
        }

        var position = line.Start + (request.Column - 1);
        var profile = string.IsNullOrWhiteSpace(request.PolicyProfile) ? RefactoringOperationOrchestrator.PolicyProfileDefault : request.PolicyProfile.Trim();
        var discovered = await owner.DiscoverActionsAtPositionAsync(document, position, selectionStart, selectionLength, ct).ConfigureAwait(false);
        var actions = discovered
            .OrderBy(static item => item.FilePath, StringComparer.Ordinal)
            .ThenBy(static item => item.SpanStart)
            .ThenBy(static item => item.SpanLength)
            .ThenBy(static item => item.Title, StringComparer.Ordinal)
            .ThenBy(static item => item.Category, StringComparer.Ordinal)
            .ThenBy(static item => item.ProviderActionKey, StringComparer.Ordinal)
            .Select(item =>
            {
                var policy = owner._refactoringPolicyService.Evaluate(item, profile);
                var actionId = owner._actionIdentityService.Create(workspaceVersion, profile, item);
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
        owner.LogActionPipelineFlow(operation, actionOrigin, actionType, policyDecision, startedAt, successCode, affectedDocumentCount);

        return new GetRefactoringsAtPositionResult(actions);
    }

    public async Task<PreviewRefactoringResult> PreviewRefactoringAsync(PreviewRefactoringRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        const string operationName = "preview_refactoring";

        var identity = owner._actionIdentityService.Parse(request.ActionId);
        if (identity == null)
        {
            var invalid = new PreviewRefactoringResult(
                string.Empty,
                string.Empty,
                Array.Empty<ChangedFilePreview>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.ActionNotFound,
                    "actionId is invalid or unsupported.",
                    ("operation", "preview_refactoring")));
            owner.LogActionPipelineFlow(operationName, "unknown", "unknown", "n/a", startedAt, ErrorCodes.ActionNotFound, 0);
            return invalid;
        }

        var (solution, workspaceVersion, error) = await owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, "n/a", startedAt, error?.Code ?? ErrorCodes.InternalError, 0);
            return new PreviewRefactoringResult(string.Empty, string.Empty, Array.Empty<ChangedFilePreview>(), error);
        }

        if (workspaceVersion != identity.WorkspaceVersion)
        {
            var stale = new PreviewRefactoringResult(
                request.ActionId,
                string.Empty,
                Array.Empty<ChangedFilePreview>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.WorkspaceChanged,
                    "Workspace changed since actionId was produced.",
                    ("operation", "preview_refactoring")));
            owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, "n/a", startedAt, ErrorCodes.WorkspaceChanged, 0);
            return stale;
        }

        var actionOperation = await owner.TryBuildActionOperationAsync(solution, identity, ct).ConfigureAwait(false);
        if (actionOperation == null)
        {
            var notFound = new PreviewRefactoringResult(
                request.ActionId,
                string.Empty,
                Array.Empty<ChangedFilePreview>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.ActionNotFound,
                    "No matching refactoring action found for actionId.",
                    ("operation", "preview_refactoring")));
            owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, "n/a", startedAt, ErrorCodes.ActionNotFound, 0);
            return notFound;
        }

        var preview = await actionOperation.ApplyAsync(solution, ct).ConfigureAwait(false);
        var changedFiles = await solution.CollectChangedFilesAsync(preview, ct).ConfigureAwait(false);
        owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, "n/a", startedAt, "ok", changedFiles.Count);
        return new PreviewRefactoringResult(request.ActionId, actionOperation.Title, changedFiles);
    }

    public async Task<ApplyRefactoringResult> ApplyRefactoringAsync(ApplyRefactoringRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        const string operationName = "apply_refactoring";

        var identity = owner._actionIdentityService.Parse(request.ActionId);
        if (identity == null)
        {
            var invalid = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.ActionNotFound,
                    "actionId is invalid or unsupported.",
                    ("operation", "apply_refactoring")));
            owner.LogActionPipelineFlow(operationName, "unknown", "unknown", "n/a", startedAt, ErrorCodes.ActionNotFound, 0);
            return invalid;
        }

        var policy = owner._refactoringPolicyService.Evaluate(identity.ToDiscoveredAction(), identity.PolicyProfile);
        if (!string.Equals(policy.Decision, "allow", StringComparison.Ordinal))
        {
            var blocked = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.PolicyBlocked,
                    policy.ReasonMessage,
                    ("operation", "apply_refactoring"),
                    ("policyDecision", policy.Decision),
                    ("policyReasonCode", policy.ReasonCode)));
            owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, ErrorCodes.PolicyBlocked, 0);
            return blocked;
        }

        var (solution, workspaceVersion, error) = await owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, error?.Code ?? ErrorCodes.InternalError, 0);
            return new ApplyRefactoringResult(request.ActionId, 0, Array.Empty<string>(), error);
        }

        if (workspaceVersion != identity.WorkspaceVersion)
        {
            var stale = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.WorkspaceChanged,
                    "Workspace changed since actionId was produced.",
                    ("operation", "apply_refactoring")));
            owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, ErrorCodes.WorkspaceChanged, 0);
            return stale;
        }

        var actionOperation = await owner.TryBuildActionOperationAsync(solution, identity, ct).ConfigureAwait(false);
        if (actionOperation == null)
        {
            var notFound = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.ActionNotFound,
                    "No matching refactoring action found for actionId.",
                    ("operation", "apply_refactoring")));
            owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, ErrorCodes.ActionNotFound, 0);
            return notFound;
        }

        var updated = await actionOperation.ApplyAsync(solution, ct).ConfigureAwait(false);
        var changedFiles = await solution.CollectChangedFilesAsync(updated, ct).ConfigureAwait(false);
        if (changedFiles.Count == 0)
        {
            var conflict = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.FixConflict,
                    "Refactoring produced no changes to apply.",
                    ("operation", "apply_refactoring")));
            owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, ErrorCodes.FixConflict, 0);
            return conflict;
        }

        var (applied, applyError) = await owner._solutionAccessor.TryApplySolutionAsync(updated, ct).ConfigureAwait(false);
        if (!applied)
        {
            var applyFailed = new ApplyRefactoringResult(
                request.ActionId,
                0,
                Array.Empty<string>(),
                applyError ?? RefactoringOperationExtensions.CreateError(ErrorCodes.InternalError, "Failed to apply refactoring changes."));
            owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, applyFailed.Error?.Code ?? ErrorCodes.InternalError, 0);
            return applyFailed;
        }

        var paths = changedFiles.Select(static item => item.FilePath).ToArray();
        owner.LogActionPipelineFlow(operationName, identity.Origin, identity.Category, policy.Decision, startedAt, "ok", paths.Length);
        return new ApplyRefactoringResult(request.ActionId, paths.Length, paths);
    }
}

internal sealed class CodeFixOperations
{
    private readonly RefactoringOperationOrchestrator _owner;

    /// <summary>
    /// Gets and applies code fixes from Roslyn analyzers at text positions.
    /// </summary>
    public CodeFixOperations(RefactoringOperationOrchestrator owner)
    {
        _owner = owner;
    }

    public async Task<GetCodeFixesResult> GetCodeFixesAsync(GetCodeFixesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var requestValidationError = request.ValidateGetCodeFixes();
        if (requestValidationError != null)
        {
            return requestValidationError;
        }

        var (solution, version, error) = await _owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new GetCodeFixesResult(Array.Empty<CodeFixDescriptor>(), error);
        }

        var documents = solution.ResolveScopeDocuments(request.Scope, request.Path).ToArray();
        if (documents.Length == 0)
        {
            return new GetCodeFixesResult(Array.Empty<CodeFixDescriptor>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.PathOutOfScope,
                    "The provided path is outside the selected solution scope.",
                    ("path", request.Path),
                    ("operation", "get_code_fixes")));
        }

        var diagnosticFilter = request.DiagnosticIds.ToDiagnosticFilter();
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
                if (!diagnostic.IsSupportedDiagnostic())
                {
                    continue;
                }

                if (diagnosticFilter != null && !diagnosticFilter.Contains(diagnostic.Id))
                {
                    continue;
                }

                if (categoryFilter != null && !string.Equals(RefactoringOperationOrchestrator.SupportedFixCategory, categoryFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var declaration = await document.TryGetUnusedLocalDeclarationAsync(diagnostic, ct).ConfigureAwait(false);
                if (declaration == null)
                {
                    continue;
                }

                var fix = declaration.ToFixDescriptor(document, diagnostic, version);
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

        var parse = request.FixId.ParseFixId();
        if (parse == null)
        {
            return RefactoringOperationExtensions.CreatePreviewError(ErrorCodes.FixNotFound, "fixId is invalid or unsupported.");
        }

        var (solution, version, error) = await _owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return RefactoringOperationExtensions.CreatePreviewError(error ?? new ErrorInfo(ErrorCodes.InternalError, "Unable to access the current solution."));
        }

        if (version != parse.WorkspaceVersion)
        {
            return RefactoringOperationExtensions.CreatePreviewError(ErrorCodes.WorkspaceChanged, "Workspace changed since fixId was produced.");
        }

        var operation = await _owner.TryBuildFixOperationAsync(solution, parse, ct).ConfigureAwait(false);
        if (operation == null)
        {
            return RefactoringOperationExtensions.CreatePreviewError(ErrorCodes.FixNotFound, "No matching code fix found for fixId.");
        }

        var previewSolution = await operation.ApplyAsync(solution, ct).ConfigureAwait(false);
        var changedFiles = await solution.CollectChangedFilesAsync(previewSolution, ct).ConfigureAwait(false);

        return new PreviewCodeFixResult(
            request.FixId,
            operation.Title,
            changedFiles);
    }

    public async Task<ApplyCodeFixResult> ApplyCodeFixAsync(ApplyCodeFixRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var parse = request.FixId.ParseFixId();
        if (parse == null)
        {
            return RefactoringOperationExtensions.CreateApplyError(request.FixId, ErrorCodes.FixNotFound, "fixId is invalid or unsupported.");
        }

        var (solution, version, error) = await _owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ApplyCodeFixResult(request.FixId, 0, Array.Empty<string>(), error);
        }

        if (version != parse.WorkspaceVersion)
        {
            return RefactoringOperationExtensions.CreateApplyError(request.FixId, ErrorCodes.WorkspaceChanged, "Workspace changed since fixId was produced.");
        }

        var operation = await _owner.TryBuildFixOperationAsync(solution, parse, ct).ConfigureAwait(false);
        if (operation == null)
        {
            return RefactoringOperationExtensions.CreateApplyError(request.FixId, ErrorCodes.FixNotFound, "No matching code fix found for fixId.");
        }

        var updatedSolution = await operation.ApplyAsync(solution, ct).ConfigureAwait(false);
        var changedFiles = await solution.CollectChangedFilesAsync(updatedSolution, ct).ConfigureAwait(false);
        if (changedFiles.Count == 0)
        {
            return RefactoringOperationExtensions.CreateApplyError(request.FixId, ErrorCodes.FixConflict, "Code fix could not produce any workspace changes.");
        }

        var (applied, applyError) = await _owner._solutionAccessor.TryApplySolutionAsync(updatedSolution, ct).ConfigureAwait(false);
        if (!applied)
        {
            return new ApplyCodeFixResult(request.FixId, 0, Array.Empty<string>(),
                applyError ?? RefactoringOperationExtensions.CreateError(ErrorCodes.InternalError, "Failed to apply code fix changes."));
        }

        var paths = changedFiles.Select(static file => file.FilePath).ToArray();
        return new ApplyCodeFixResult(request.FixId, paths.Length, paths);
    }
}

internal sealed class CleanupOperations
{
    private readonly RefactoringOperationOrchestrator _owner;

    /// <summary>
    /// Executes code cleanup: removes unused usings, organizes imports, formats document.
    /// </summary>
    public CleanupOperations(RefactoringOperationOrchestrator owner)
    {
        _owner = owner;
    }

    public async Task<ExecuteCleanupResult> ExecuteCleanupAsync(ExecuteCleanupRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var requestValidationError = request.ValidateExecuteCleanup();
        if (requestValidationError != null)
        {
            return requestValidationError;
        }

        var (solution, version, error) = await _owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ExecuteCleanupResult(request.Scope, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), error);
        }

        var effectiveExpectedWorkspaceVersion = request.ExpectedWorkspaceVersion;

        var scopedDocuments = solution.ResolveScopeDocuments(request.Scope, request.Path)
            .OrderBy(static d => d.FilePath ?? d.Name, StringComparer.Ordinal)
            .ToArray();
        if (scopedDocuments.Length == 0)
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                RefactoringOperationExtensions.CreateError(ErrorCodes.PathOutOfScope,
                    "The provided path is outside the selected solution scope.",
                    ("operation", "execute_cleanup"),
                    ("path", request.Path)));
        }

        const bool healthCheckPerformed = true;
        var autoReloadAttempted = false;
        var autoReloadSucceeded = false;
        var health = await WorkspaceDocumentFilesystemHealthEvaluator.EvaluateAsync(scopedDocuments, ct).ConfigureAwait(false);
        if (!health.IsConsistent)
        {
            if (_owner._solutionAccessor is ISolutionSessionService sessionService)
            {
                autoReloadAttempted = true;
                var reload = await sessionService.ReloadSolutionAsync(new ReloadSolutionRequest(), ct).ConfigureAwait(false);
                autoReloadSucceeded = reload.Success;
                if (reload.Success)
                {
                    var (reloadedSolution, reloadedVersion, reloadError) = await _owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
                    if (reloadedSolution == null)
                    {
                        return RefactoringOperationExtensions.CreateStaleWorkspaceResult(
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

                    scopedDocuments = solution.ResolveScopeDocuments(request.Scope, request.Path)
                        .OrderBy(static d => d.FilePath ?? d.Name, StringComparer.Ordinal)
                        .ToArray();
                    if (scopedDocuments.Length == 0)
                    {
                        return new ExecuteCleanupResult(
                            request.Scope,
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            RefactoringOperationExtensions.BuildCleanupMetadataWarnings(healthCheckPerformed, autoReloadAttempted, autoReloadSucceeded),
                            RefactoringOperationExtensions.CreateError(ErrorCodes.PathOutOfScope,
                                "The provided path is outside the selected solution scope.",
                                ("operation", "execute_cleanup"),
                                ("path", request.Path)));
                    }

                    health = await WorkspaceDocumentFilesystemHealthEvaluator.EvaluateAsync(scopedDocuments, ct).ConfigureAwait(false);
                }
                else
                {
                    return RefactoringOperationExtensions.CreateStaleWorkspaceResult(
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
                return RefactoringOperationExtensions.CreateStaleWorkspaceResult(
                    request.Scope,
                    healthCheckPerformed,
                    autoReloadAttempted,
                    autoReloadSucceeded,
                    health.MissingRootedFiles.Count);
            }
        }

        var cleanupMetadataWarnings = RefactoringOperationExtensions.BuildCleanupMetadataWarnings(healthCheckPerformed, autoReloadAttempted, autoReloadSucceeded);

        if (effectiveExpectedWorkspaceVersion.HasValue && effectiveExpectedWorkspaceVersion.Value != version)
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                cleanupMetadataWarnings,
                RefactoringOperationExtensions.CreateError(ErrorCodes.WorkspaceChanged,
                    "Workspace changed before cleanup started.",
                    ("operation", "execute_cleanup")));
        }

        var updated = solution;
        updated = await _owner.ApplyDiagnosticCleanupStepAsync(updated, scopedDocuments, RefactoringOperationOrchestrator.CleanupRemoveUnusedUsingDiagnostics, ct).ConfigureAwait(false);
        updated = await _owner.OrganizeUsingsAsync(updated, scopedDocuments, ct).ConfigureAwait(false);
        updated = await _owner.ApplyDiagnosticCleanupStepAsync(updated, scopedDocuments, RefactoringOperationOrchestrator.CleanupModifierOrderDiagnostics, ct).ConfigureAwait(false);
        updated = await _owner.ApplyDiagnosticCleanupStepAsync(updated, scopedDocuments, RefactoringOperationOrchestrator.CleanupReadonlyDiagnostics, ct).ConfigureAwait(false);
        updated = await _owner.FormatScopeAsync(updated, scopedDocuments, ct).ConfigureAwait(false);

        var changedFiles = await solution.CollectChangedFilesAsync(updated, ct).ConfigureAwait(false);
        var changedPaths = changedFiles.Select(static file => file.FilePath).ToArray();
        if (changedPaths.Length == 0)
        {
            return new ExecuteCleanupResult(
                request.Scope,
                RefactoringOperationExtensions.BuildCleanupRuleIds(),
                Array.Empty<string>(),
                cleanupMetadataWarnings);
        }

        var (applyVersion, versionError) = await _owner._solutionAccessor.GetWorkspaceVersionAsync(ct).ConfigureAwait(false);
        if (versionError != null)
        {
            return new ExecuteCleanupResult(request.Scope, RefactoringOperationExtensions.BuildCleanupRuleIds(), Array.Empty<string>(), cleanupMetadataWarnings, versionError);
        }

        if (effectiveExpectedWorkspaceVersion.HasValue && effectiveExpectedWorkspaceVersion.Value != applyVersion)
        {
            return new ExecuteCleanupResult(
                request.Scope,
                RefactoringOperationExtensions.BuildCleanupRuleIds(),
                Array.Empty<string>(),
                cleanupMetadataWarnings,
                RefactoringOperationExtensions.CreateError(ErrorCodes.WorkspaceChanged,
                    "Workspace changed during cleanup execution.",
                    ("operation", "execute_cleanup")));
        }

        var (applied, applyError) = await _owner._solutionAccessor.TryApplySolutionAsync(updated, ct).ConfigureAwait(false);
        if (!applied)
        {
            return new ExecuteCleanupResult(
                request.Scope,
                RefactoringOperationExtensions.BuildCleanupRuleIds(),
                Array.Empty<string>(),
                cleanupMetadataWarnings,
                applyError ?? RefactoringOperationExtensions.CreateError(ErrorCodes.InternalError, "Failed to apply cleanup changes.", ("operation", "execute_cleanup")));
        }

        return new ExecuteCleanupResult(request.Scope, RefactoringOperationExtensions.BuildCleanupRuleIds(), changedPaths, cleanupMetadataWarnings);
    }
}

internal sealed class RenameOperations
{
    private readonly RefactoringOperationOrchestrator _owner;

    /// <summary>
    /// Renames symbols with proper update of all references.
    /// </summary>
    public RenameOperations(RefactoringOperationOrchestrator owner)
    {
        _owner = owner;
    }

    public Task<RenameSymbolResult> RenameSymbolAsync(RenameSymbolRequest request, CancellationToken ct)
        => RenameSymbolAsync(request, ct, allowReloadFallback: true);

    private async Task<RenameSymbolResult> RenameSymbolAsync(RenameSymbolRequest request, CancellationToken ct, bool allowReloadFallback)
    {
        ArgumentNullException.ThrowIfNull(request);

        ct.ThrowIfCancellationRequested();

        var symbolInternalId = RefactoringOperationOrchestrator.NormalizeInputSymbolId(request.SymbolId) ?? request.SymbolId;
        var symbolId = symbolInternalId.NormalizeAcceptedSymbolIdForOutput();

        var invalidInputError = RefactoringOperationExtensions.TryCreateInvalidSymbolIdError(request.SymbolId, "rename-symbol");
        if (invalidInputError != null)
            return RefactoringOperationExtensions.CreateErrorResult(invalidInputError);

        if (string.IsNullOrWhiteSpace(request.NewName))
        {
            return RefactoringOperationExtensions.CreateErrorResult(ErrorCodes.InvalidNewName,
                "New name must be provided.",
                ("newName", request.NewName),
                ("operation", "rename-symbol"));
        }

        try
        {
            var (solution, error) = await _owner.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return RefactoringOperationExtensions.CreateErrorResult(error ?? new ErrorInfo(ErrorCodes.InternalError, "Unable to access the current solution."));
            }

            var symbol = await _owner.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return RefactoringOperationExtensions.CreateErrorResult(ErrorCodes.SymbolNotFound,
                    $"Symbol '{symbolId}' could not be resolved.",
                    ("symbolId", symbolId),
                    ("operation", "rename-symbol"));
            }

            if (!request.NewName.IsValidIdentifier(symbol))
            {
                return RefactoringOperationExtensions.CreateErrorResult(ErrorCodes.InvalidNewName,
                    $"'{request.NewName}' is not a valid identifier.",
                    ("newName", request.NewName),
                    ("operation", "rename-symbol"));
            }

            if (symbol.WouldConflict(request.NewName))
            {
                return RefactoringOperationExtensions.CreateErrorResult(ErrorCodes.RenameConflict,
                    $"Renaming '{symbol.Name}' to '{request.NewName}' would conflict with an existing symbol.",
                    ("symbolId", symbolId),
                    ("newName", request.NewName),
                    ("operation", "rename-symbol"));
            }

            var declarationKeys = symbol.GetSourceLocationKeys();
            var affectedLocations = await symbol.CollectAffectedLocationsAsync(solution, ct).ConfigureAwait(false);
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
            var (applied, applyError) = await _owner._solutionAccessor.TryApplySolutionAsync(renamedSolution, ct).ConfigureAwait(false);
            if (!applied)
            {
                if (allowReloadFallback && _owner._solutionAccessor is ISolutionSessionService sessionService)
                {
                    var reload = await sessionService.ReloadSolutionAsync(new ReloadSolutionRequest(), ct).ConfigureAwait(false);
                    if (reload.Success)
                    {
                        return await RenameSymbolAsync(request, ct, allowReloadFallback: false).ConfigureAwait(false);
                    }
                }

                return RefactoringOperationExtensions.CreateErrorResult(applyError ??
                    RefactoringOperationExtensions.CreateError(ErrorCodes.InternalError,
                        "Failed to update the active solution after rename.",
                        ("symbolId", symbolId),
                        ("newName", request.NewName),
                        ("operation", "rename-symbol")));
            }

            var renamedSymbol = await _owner.TryResolveRenamedSymbolAsync(renamedSolution, request.NewName, declarationKeys, ct)
                .ConfigureAwait(false);
            var renamedSymbolInternalId = renamedSymbol != null ? RefactoringSymbolIdentity.CreateId(renamedSymbol) : RefactoringSymbolIdentity.CreateId(symbol);
            symbolInternalId.Update(renamedSymbolInternalId);
            var renamedSymbolId = symbolId;

            return new RenameSymbolResult(
                renamedSymbolId,
                changedDocumentIds.Count,
                affectedLocations,
                changedFiles);
        }
        catch (ArgumentException ex)
        {
            return RefactoringOperationExtensions.CreateErrorResult(ErrorCodes.InvalidNewName,
                $"'{request.NewName}' is invalid: {ex.Message}",
                ("newName", request.NewName),
                ("operation", "rename-symbol"));
        }
        catch (InvalidOperationException ex)
        {
                return RefactoringOperationExtensions.CreateErrorResult(ErrorCodes.RenameConflict,
                $"Rename conflict: {ex.Message}",
                ("symbolId", symbolId),
                ("newName", request.NewName),
                ("operation", "rename-symbol"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _owner._logger.LogError(ex, "RenameSymbol failed for {SymbolId}", request.SymbolId);
            return RefactoringOperationExtensions.CreateErrorResult(ErrorCodes.InternalError,
                $"Failed to rename symbol '{request.SymbolId}': {ex.Message}",
                ("symbolId", symbolId),
                ("newName", request.NewName),
                ("operation", "rename-symbol"));
        }
    }
}

internal sealed class DocumentFormattingOperations
{
    private readonly RefactoringOperationOrchestrator _owner;

    public DocumentFormattingOperations(RefactoringOperationOrchestrator owner)
    {
        _owner = owner;
    }

    public async Task<FormatDocumentResult> FormatDocumentAsync(FormatDocumentRequest request, CancellationToken ct)
        => await FormatDocumentAsync(request, ct, allowReloadFallback: true).ConfigureAwait(false);

    private async Task<FormatDocumentResult> FormatDocumentAsync(FormatDocumentRequest request, CancellationToken ct, bool allowReloadFallback)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var validationError = request.ValidateFormatDocument();
        if (validationError != null)
        {
            return validationError;
        }

        try
        {
            var requestedPath = request.Path.Trim();
            var (solution, version, error) = await _owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return RefactoringOperationExtensions.CreateFormatDocumentErrorResult(requestedPath, error);
            }

            var document = solution.FindDocument(requestedPath);
            if (document == null)
            {
                return RefactoringOperationExtensions.CreateFormatDocumentErrorResult(
                    requestedPath,
                    ErrorCodes.PathOutOfScope,
                    "The provided path does not match a document in the selected solution scope.",
                    ("path", requestedPath),
                    ("operation", "format_document"));
            }

            var health = await WorkspaceDocumentFilesystemHealthEvaluator.EvaluateAsync([document], ct).ConfigureAwait(false);
            if (!health.IsConsistent)
            {
                if (allowReloadFallback && _owner._solutionAccessor is ISolutionSessionService sessionService)
                {
                    var reload = await sessionService.ReloadSolutionAsync(new ReloadSolutionRequest(), ct).ConfigureAwait(false);
                    if (reload.Success)
                    {
                        return await FormatDocumentAsync(request, ct, allowReloadFallback: false).ConfigureAwait(false);
                    }

                    return RefactoringOperationExtensions.CreateFormatDocumentErrorResult(
                        requestedPath,
                        ErrorCodes.StaleWorkspaceSnapshot,
                        RefactoringOperationOrchestrator.CleanupStaleWorkspaceMessage,
                        ("operation", "format_document"),
                        (RefactoringOperationOrchestrator.CleanupHealthCheckPerformedDetail, bool.TrueString.ToLowerInvariant()),
                        (RefactoringOperationOrchestrator.CleanupAutoReloadAttemptedDetail, bool.TrueString.ToLowerInvariant()),
                        (RefactoringOperationOrchestrator.CleanupAutoReloadSucceededDetail, bool.FalseString.ToLowerInvariant()),
                        (RefactoringOperationOrchestrator.CleanupMissingFileCountDetail, health.MissingRootedFiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        (RefactoringOperationOrchestrator.CleanupReloadErrorCodeDetail, reload.Error?.Code));
                }

                return RefactoringOperationExtensions.CreateFormatDocumentErrorResult(
                    requestedPath,
                    ErrorCodes.StaleWorkspaceSnapshot,
                    RefactoringOperationOrchestrator.CleanupStaleWorkspaceMessage,
                    ("operation", "format_document"),
                    (RefactoringOperationOrchestrator.CleanupHealthCheckPerformedDetail, bool.TrueString.ToLowerInvariant()),
                    (RefactoringOperationOrchestrator.CleanupAutoReloadAttemptedDetail, bool.FalseString.ToLowerInvariant()),
                    (RefactoringOperationOrchestrator.CleanupAutoReloadSucceededDetail, bool.FalseString.ToLowerInvariant()),
                    (RefactoringOperationOrchestrator.CleanupMissingFileCountDetail, health.MissingRootedFiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            var updated = await _owner.FormatScopeAsync(solution, [document], ct).ConfigureAwait(false);
            var changedFiles = await solution.CollectChangedFilesAsync(updated, ct).ConfigureAwait(false);
            var documentPath = document.FilePath ?? document.Name;
            if (changedFiles.Count == 0)
            {
                return new FormatDocumentResult(documentPath, false);
            }

            var (applyVersion, versionError) = await _owner._solutionAccessor.GetWorkspaceVersionAsync(ct).ConfigureAwait(false);
            if (versionError != null)
            {
                return RefactoringOperationExtensions.CreateFormatDocumentErrorResult(documentPath, versionError);
            }

            if (applyVersion != version)
            {
                return RefactoringOperationExtensions.CreateFormatDocumentErrorResult(
                    documentPath,
                    ErrorCodes.WorkspaceChanged,
                    "Workspace changed during format_document execution.",
                    ("path", documentPath),
                    ("operation", "format_document"));
            }

            var (applied, applyError) = await _owner._solutionAccessor.TryApplySolutionAsync(updated, ct).ConfigureAwait(false);
            if (!applied)
            {
                return RefactoringOperationExtensions.CreateFormatDocumentErrorResult(
                    documentPath,
                    applyError ?? RefactoringOperationExtensions.CreateError(
                        ErrorCodes.InternalError,
                        "Failed to apply formatted document changes.",
                        ("path", documentPath),
                        ("operation", "format_document")));
            }

            return new FormatDocumentResult(documentPath, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _owner._logger.LogError(ex, "FormatDocument failed for {Path}", request.Path);
            return RefactoringOperationExtensions.CreateFormatDocumentErrorResult(
                request.Path,
                ErrorCodes.InternalError,
                $"Failed to format document '{request.Path}': {ex.Message}",
                ("path", request.Path),
                ("operation", "format_document"));
        }
    }
}
