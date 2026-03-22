using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Mutation;

namespace RoslynMcp.Tools.Mutation.ReplaceMethod;

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

        var (target, resolveError) = await _targetResolver.ResolveMethodAsync(request.TargetMethodSymbolId, session.Solution, "replace_method", cancellationToken).ConfigureAwait(false);
        if (target is null)
            return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), resolveError);

        var spec = request.ToSpec();
        if (!_builder.TryBuild(spec, out var replacementMethod, out var builderError) || replacementMethod is null)
            return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), builderError);

        if (MethodSignatureComparer.HasEquivalentMethod(target.MethodSymbol.ContainingType, spec, target.MethodSymbol))
            return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("method_conflict", $"An equivalent method '{request.Name}' already exists on '{target.MethodSymbol.ContainingType.ToDisplayString()}'."));

        var root = await target.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return new Result("failed", [], request.TargetMethodSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("internal_error", "Failed to load the target document syntax tree."));

        var updatedRoot = root.ReplaceNode(target.Declaration, replacementMethod.WithTriviaFrom(target.Declaration));
        var updatedDocument = target.Document.WithSyntaxRoot(updatedRoot);
        var updatedSolution = await updatedDocument.FormatDocumentAsync(cancellationToken).ConfigureAwait(false);
        var changedFiles = await session.Solution.CollectChangedFilesAsync(updatedSolution, cancellationToken).ConfigureAwait(false);
        var diagnosticsDelta = await DiagnosticsDeltaService.GetDeltaAsync(session.Solution, updatedSolution, target.Document.Id, cancellationToken).ConfigureAwait(false);

        var containingType = await SymbolLookup.ResolveSymbolAsync(target.MethodSymbol.ContainingType.ToStableId(), updatedSolution, cancellationToken).ConfigureAwait(false) as INamedTypeSymbol;
        var method = containingType?.GetMembers(request.Name).OfType<IMethodSymbol>().FirstOrDefault(candidate => MethodSignatureComparer.MatchesMethodSignature(candidate, spec));
        if (method is null)
            return new Result("failed", changedFiles, request.TargetMethodSymbolId, null, diagnosticsDelta, new ErrorInfo("created_symbol_unresolved", "The replaced method could not be resolved after mutation.")).WithWorkspaceRelativePaths();

        if (!session.Workspace.TryApplyChanges(updatedSolution))
            return new Result("failed", changedFiles, request.TargetMethodSymbolId, null, diagnosticsDelta, new ErrorInfo("internal_error", "Failed to apply replace_method changes.")).WithWorkspaceRelativePaths();

        return new Result("applied", changedFiles, request.TargetMethodSymbolId, new ReplacedMethodInfo(request.TargetMethodSymbolId, target.MethodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), method.ToStableId(), method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)), diagnosticsDelta).WithWorkspaceRelativePaths();
    }
}
