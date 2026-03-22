using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Infrastructure.Services;
using RoslynMcp.Tools.Mutation;

namespace RoslynMcp.Tools.Mutation.AddMethod;

public sealed class Service(RoslynMcp.Tools.Infrastructure.Services.Workspace workspace)
{
	private readonly TargetResolver _targetResolver = new();
    private readonly MethodDeclarationBuilder _builder = new();
    private readonly DiagnosticsDeltaService _diagnosticsDeltaService = new();

    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        var session = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
            return new Result("failed", [], request.TargetTypeSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("no_solution_loaded", "No solution is currently loaded."));

        var (target, resolveError) = await _targetResolver.ResolveTypeAsync(request.TargetTypeSymbolId, session.Solution, "add_method", cancellationToken).ConfigureAwait(false);
        if (target is null)
            return new Result("failed", [], request.TargetTypeSymbolId, null, new DiagnosticsDeltaInfo([], []), resolveError);

        var spec = request.ToSpec();
        if (!_builder.TryBuild(spec, out var methodDeclaration, out var builderError) || methodDeclaration is null)
            return new Result("failed", [], request.TargetTypeSymbolId, null, new DiagnosticsDeltaInfo([], []), builderError);

        if (MethodSignatureComparer.HasEquivalentMethod(target.TypeSymbol, spec))
            return new Result("failed", [], request.TargetTypeSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("method_conflict", $"An equivalent method '{request.Name}' already exists on '{target.TypeSymbol.ToDisplayString()}'."));

        var root = await target.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return new Result("failed", [], request.TargetTypeSymbolId, null, new DiagnosticsDeltaInfo([], []), new ErrorInfo("internal_error", "Failed to load the target document syntax tree."));

        var updatedRoot = root.ReplaceNode(target.Declaration, target.Declaration.AddMembers(methodDeclaration));
        var updatedDocument = target.Document.WithSyntaxRoot(updatedRoot);
        var updatedSolution = await updatedDocument.FormatDocumentAsync(cancellationToken).ConfigureAwait(false);
        var changedFiles = await session.Solution.CollectChangedFilesAsync(updatedSolution, cancellationToken).ConfigureAwait(false);
        var diagnosticsDelta = await DiagnosticsDeltaService.GetDeltaAsync(session.Solution, updatedSolution, target.Document.Id, cancellationToken).ConfigureAwait(false);

        var resolvedType = await SymbolLookup.ResolveSymbolAsync(request.TargetTypeSymbolId, updatedSolution, cancellationToken).ConfigureAwait(false) as INamedTypeSymbol;
        var method = resolvedType?.GetMembers(request.Name).OfType<IMethodSymbol>().FirstOrDefault(candidate => MethodSignatureComparer.MatchesMethodSignature(candidate, spec));
        if (method is null)
            return new Result("failed", changedFiles, request.TargetTypeSymbolId, null, diagnosticsDelta, new ErrorInfo("created_symbol_unresolved", "The inserted method could not be resolved after mutation.")).WithWorkspaceRelativePaths();

        if (!await workspace.ApplyChangesAsync(updatedSolution, cancellationToken).ConfigureAwait(false))
            return new Result("failed", changedFiles, request.TargetTypeSymbolId, null, diagnosticsDelta, new ErrorInfo("internal_error", "Failed to apply add_method changes.")).WithWorkspaceRelativePaths();

        return new Result("applied", changedFiles, request.TargetTypeSymbolId, new AddedMethodInfo(method.ToStableId(), method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)), diagnosticsDelta).WithWorkspaceRelativePaths();
    }
}
