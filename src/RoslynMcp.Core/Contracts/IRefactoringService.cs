using RoslynMcp.Core.Models.Refactoring;

namespace RoslynMcp.Core.Contracts;

public interface IRefactoringService
{
    Task<GetRefactoringsAtPositionResult> GetRefactoringsAtPositionAsync(GetRefactoringsAtPositionRequest request, CancellationToken ct);
    Task<PreviewRefactoringResult> PreviewRefactoringAsync(PreviewRefactoringRequest request, CancellationToken ct);
    Task<ApplyRefactoringResult> ApplyRefactoringAsync(ApplyRefactoringRequest request, CancellationToken ct);
    Task<GetCodeFixesResult> GetCodeFixesAsync(GetCodeFixesRequest request, CancellationToken ct);
    Task<PreviewCodeFixResult> PreviewCodeFixAsync(PreviewCodeFixRequest request, CancellationToken ct);
    Task<ApplyCodeFixResult> ApplyCodeFixAsync(ApplyCodeFixRequest request, CancellationToken ct);
    Task<ExecuteCleanupResult> ExecuteCleanupAsync(ExecuteCleanupRequest request, CancellationToken ct);
    Task<RenameSymbolResult> RenameSymbolAsync(RenameSymbolRequest request, CancellationToken ct);
}
