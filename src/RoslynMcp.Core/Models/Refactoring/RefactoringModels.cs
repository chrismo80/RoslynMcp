using RoslynMcp.Core.Models.Common;

namespace RoslynMcp.Core.Models.Refactoring;

public sealed record RenameSymbolRequest(string SymbolId, string NewName);

public sealed record RenameSymbolResult(
    string? RenamedSymbolId,
    int ChangedDocumentCount,
    IReadOnlyList<SourceLocation> AffectedLocations,
    IReadOnlyList<string> ChangedFiles,
    ErrorInfo? Error = null);

public sealed record GetCodeFixesRequest(
    string Scope,
    string? Path = null,
    IReadOnlyList<string>? DiagnosticIds = null,
    string? Category = null);

public sealed record CodeFixDescriptor(
    string FixId,
    string Title,
    string DiagnosticId,
    string Category,
    SourceLocation Location,
    string FilePath);

public sealed record GetCodeFixesResult(IReadOnlyList<CodeFixDescriptor> Fixes, ErrorInfo? Error = null);

public sealed record ChangedFilePreview(string FilePath, int EditCount);

public sealed record PreviewCodeFixRequest(string FixId);

public sealed record PreviewCodeFixResult(
    string FixId,
    string Title,
    IReadOnlyList<ChangedFilePreview> Changes,
    ErrorInfo? Error = null);

public sealed record ApplyCodeFixRequest(string FixId);

public sealed record ApplyCodeFixResult(
    string FixId,
    int ChangedDocumentCount,
    IReadOnlyList<string> ChangedFiles,
    ErrorInfo? Error = null);

public sealed record ExecuteCleanupRequest(
    string Scope,
    string? Path = null,
    string? PolicyProfile = null,
    int? ExpectedWorkspaceVersion = null);

public sealed record ExecuteCleanupResult(
    string Scope,
    IReadOnlyList<string> AppliedRuleIds,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> Warnings,
    ErrorInfo? Error = null);

public sealed record PolicyDecisionInfo(string Decision, string ReasonCode, string ReasonMessage);

public sealed record RefactoringActionDescriptor(
    string ActionId,
    string Title,
    string Category,
    string Origin,
    string RiskLevel,
    PolicyDecisionInfo PolicyDecision,
    SourceLocation Location,
    string? DiagnosticId = null,
    string? RefactoringId = null);

public sealed record GetRefactoringsAtPositionRequest(
    string Path,
    int Line,
    int Column,
    int? SelectionStart = null,
    int? SelectionLength = null,
    string? PolicyProfile = null);

public sealed record GetRefactoringsAtPositionResult(
    IReadOnlyList<RefactoringActionDescriptor> Actions,
    ErrorInfo? Error = null);

public sealed record PreviewRefactoringRequest(string ActionId);

public sealed record PreviewRefactoringResult(
    string ActionId,
    string Title,
    IReadOnlyList<ChangedFilePreview> Changes,
    ErrorInfo? Error = null);

public sealed record ApplyRefactoringRequest(string ActionId);

public sealed record ApplyRefactoringResult(
    string ActionId,
    int ChangedDocumentCount,
    IReadOnlyList<string> ChangedFiles,
    ErrorInfo? Error = null);
