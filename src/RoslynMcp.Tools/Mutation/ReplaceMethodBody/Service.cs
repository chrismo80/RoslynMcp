using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Mutation;

namespace RoslynMcp.Tools.Mutation.ReplaceMethodBody;

public sealed class Service(RoslynMcp.Tools.Infrastructure.Services.Workspace workspace)
{
	private readonly TargetResolver _targetResolver = new();
    private readonly MethodDeclarationBuilder _builder = new();
    private readonly DiagnosticsDeltaService _diagnosticsDeltaService = new();

    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
            return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("no_solution_loaded", "No solution is currently loaded."));

        var (target, resolveError) = await _targetResolver.ResolveMethodAsync(request.TargetMethodSymbolId, session.Solution, "replace_method_body", cancellationToken).ConfigureAwait(false);
        if (target is null)
            return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), resolveError);

        if (target.Declaration.Body is null || target.Declaration.ExpressionBody is not null)
            return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("target_not_source_editable", "replace_method_body currently supports only existing block-bodied methods."));

        if (!MethodDeclarationBuilder.TryParseBody(request.Body.NormalizeEscapedNewlines(), out var bodyBlock, out var bodyError) || bodyBlock is null)
            return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), bodyError);

        var root = await target.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("internal_error", "Failed to load the target document syntax tree."));

        var updatedMethod = target.Declaration.WithBody(bodyBlock.WithTriviaFrom(target.Declaration.Body));
        var updatedRoot = root.ReplaceNode(target.Declaration, updatedMethod);
        var updatedDocument = target.Document.WithSyntaxRoot(updatedRoot);
        var updatedSolution = await updatedDocument.FormatDocumentAsync(cancellationToken).ConfigureAwait(false);
        var changedFiles = await session.Solution.CollectChangedFilesAsync(updatedSolution, cancellationToken).ConfigureAwait(false);
        var diagnosticsDelta = await DiagnosticsDeltaService.GetDeltaAsync(session.Solution, updatedSolution, target.Document.Id, cancellationToken).ConfigureAwait(false);

        if (!session.Workspace.TryApplyChanges(updatedSolution))
            return new Result("failed", changedFiles, request.TargetMethodSymbolId, null, diagnosticsDelta, new ErrorInfo("internal_error", "Failed to apply replace_method_body changes.")).WithWorkspaceRelativePaths();

        return new Result("applied", changedFiles, request.TargetMethodSymbolId, new ReplacedMethodBodyInfo(request.TargetMethodSymbolId, target.MethodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)), diagnosticsDelta).WithWorkspaceRelativePaths();
    }
}
