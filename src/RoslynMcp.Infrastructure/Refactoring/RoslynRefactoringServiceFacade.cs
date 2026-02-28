using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Refactoring;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Infrastructure.Refactoring;

public sealed class RoslynRefactoringService : IRefactoringService
{
    private readonly IRefactoringOperationOrchestrator _orchestrator;

    internal RoslynRefactoringService(IRefactoringOperationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public RoslynRefactoringService(IRoslynSolutionAccessor solutionAccessor, ILogger<RoslynRefactoringService>? logger = null)
        : this(new RefactoringOperationOrchestrator(solutionAccessor, logger))
    {
    }

    public Task<GetRefactoringsAtPositionResult> GetRefactoringsAtPositionAsync(GetRefactoringsAtPositionRequest request, CancellationToken ct)
        => _orchestrator.GetRefactoringsAtPositionAsync(request, ct);

    public Task<PreviewRefactoringResult> PreviewRefactoringAsync(PreviewRefactoringRequest request, CancellationToken ct)
        => _orchestrator.PreviewRefactoringAsync(request, ct);

    public Task<ApplyRefactoringResult> ApplyRefactoringAsync(ApplyRefactoringRequest request, CancellationToken ct)
        => _orchestrator.ApplyRefactoringAsync(request, ct);

    public Task<GetCodeFixesResult> GetCodeFixesAsync(GetCodeFixesRequest request, CancellationToken ct)
        => _orchestrator.GetCodeFixesAsync(request, ct);

    public Task<PreviewCodeFixResult> PreviewCodeFixAsync(PreviewCodeFixRequest request, CancellationToken ct)
        => _orchestrator.PreviewCodeFixAsync(request, ct);

    public Task<ApplyCodeFixResult> ApplyCodeFixAsync(ApplyCodeFixRequest request, CancellationToken ct)
        => _orchestrator.ApplyCodeFixAsync(request, ct);

    public Task<ExecuteCleanupResult> ExecuteCleanupAsync(ExecuteCleanupRequest request, CancellationToken ct)
        => _orchestrator.ExecuteCleanupAsync(request, ct);

    public Task<RenameSymbolResult> RenameSymbolAsync(RenameSymbolRequest request, CancellationToken ct)
        => _orchestrator.RenameSymbolAsync(request, ct);
}
