using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.DeleteMethod;

public sealed class Service(RoslynMcp.Tools.Infrastructure.Services.Workspace workspace, SymbolLookup symbolLookup)
{
	private readonly TargetResolver _targetResolver = new(symbolLookup);
	private readonly DiagnosticsDeltaService _diagnosticsDeltaService = new();

	public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
	{
		var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
		if (session is null)
			return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("no_solution_loaded", "No solution is currently loaded."));

		var (target, resolveError) = await _targetResolver.ResolveMethodAsync(request.TargetMethodSymbolId, session.Solution, "delete_method", cancellationToken).ConfigureAwait(false);
		if (target is null)
			return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), resolveError);

		var root = await target.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		if (root is null)
			return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("internal_error", "Failed to load the target document syntax tree."));

		var updatedRoot = root.RemoveNode(target.Declaration, SyntaxRemoveOptions.KeepNoTrivia);
		if (updatedRoot is null)
			return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("target_not_source_editable", "The target method could not be removed deterministically from the document."));

		var updatedDocument = target.Document.WithSyntaxRoot(updatedRoot);
		var updatedSolution = await updatedDocument.FormatDocumentAsync(cancellationToken).ConfigureAwait(false);
		var changedFiles = await session.Solution.CollectChangedFilesAsync(updatedSolution, cancellationToken).ConfigureAwait(false);
		var diagnosticsDelta = await _diagnosticsDeltaService.GetDeltaAsync(session.Solution, updatedSolution, target.Document.Id, cancellationToken).ConfigureAwait(false);

		if (!session.Workspace.TryApplyChanges(updatedSolution))
			return new Result("failed", changedFiles, request.TargetMethodSymbolId, null, diagnosticsDelta, new ErrorInfo("internal_error", "Failed to apply delete_method changes.")).WithWorkspaceRelativePaths();

		return new Result("applied", changedFiles, request.TargetMethodSymbolId, new DeletedMethodInfo(request.TargetMethodSymbolId, target.MethodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)), diagnosticsDelta).WithWorkspaceRelativePaths();
	}
}
