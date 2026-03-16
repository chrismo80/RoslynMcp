using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Infrastructure.Navigation;

/// <summary>
/// Finds all references to a symbol within solution, project, or document scope.
/// Returns locations with line/column info.
/// </summary>
internal sealed class NavigationReferenceQueryService(
    NavigationSolutionProvider solutionProvider,
    ISymbolLookupService symbolLookupService,
    IReferenceSearchService referenceSearchService,
    ILogger logger)
{
    private readonly NavigationSolutionProvider _solutionProvider = solutionProvider ?? throw new ArgumentNullException(nameof(solutionProvider));
    private readonly ISymbolLookupService _symbolLookupService = symbolLookupService ?? throw new ArgumentNullException(nameof(symbolLookupService));
    private readonly IReferenceSearchService _referenceSearchService = referenceSearchService ?? throw new ArgumentNullException(nameof(referenceSearchService));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<FindReferencesResult> FindReferencesAsync(FindReferencesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var scopedResult = await FindReferencesCoreAsync(
            request.SymbolId,
            ReferenceScopes.Solution,
            null,
            operation: "find-references",
            ct).ConfigureAwait(false);

        return new FindReferencesResult(
            scopedResult.Symbol == null
                ? null
                : new SymbolDescriptor(scopedResult.Symbol.SymbolId, scopedResult.Symbol.Display, scopedResult.Symbol.Kind, null, null, scopedResult.Symbol.Location ?? new SourceLocation(string.Empty, 1, 1)),
            scopedResult.ReferenceFiles
                .SelectMany(static file => file.References.Select(position => new SourceLocation(file.FilePath, position.Line, position.Column)))
                .ToArray(),
            scopedResult.Error);
    }

    public async Task<FindReferencesScopedResult> FindReferencesScopedAsync(FindReferencesScopedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        return await FindReferencesCoreAsync(
            request.SymbolId,
            request.Scope,
            request.Path,
            operation: "find-references-scoped",
            ct).ConfigureAwait(false);
    }

    public async Task<FindImplementationsResult> FindImplementationsAsync(FindImplementationsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "find-implementations");
        if (invalidInputError != null)
        {
            return new FindImplementationsResult(null, [], invalidInputError);
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new FindImplementationsResult(null, [], error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new FindImplementationsResult(null, [],
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "find-implementations")));
            }

            var implementations = await _referenceSearchService.FindImplementationsAsync(symbol, solution, ct).ConfigureAwait(false);
            return new FindImplementationsResult(symbol.ToCompactSymbolSummary(), implementations.Select(MapCompactSymbolSummary).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindImplementations failed for {SymbolId}", request.SymbolId);
            return new FindImplementationsResult(null,
                Array.Empty<CompactSymbolSummary>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to find implementations '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "find-implementations")));
        }
    }

    private async Task<FindReferencesScopedResult> FindReferencesCoreAsync(
        string symbolId,
        string scope,
        string? path,
        string operation,
        CancellationToken ct)
    {
        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(symbolId, operation);
        if (invalidSymbolIdError != null)
        {
            return new FindReferencesScopedResult(null, Array.Empty<ReferenceFileGroup>(), 0, invalidSymbolIdError);
        }

        if (!_referenceSearchService.IsValidScope(scope))
        {
            return new FindReferencesScopedResult(null, [], 0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "scope must be one of: document, project, solution.",
                    ("parameter", "scope"),
                    ("operation", operation)));
        }

        if (string.Equals(scope, ReferenceScopes.Document, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(path))
        {
            return new FindReferencesScopedResult(null, [], 0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is document.",
                    ("parameter", "path"),
                    ("operation", operation)));
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new FindReferencesScopedResult(null, [], 0, error);
            }

            var (symbol, ownerProject) = await _symbolLookupService.ResolveSymbolWithProjectAsync(symbolId, solution, ct)
                .ConfigureAwait(false);
            if (symbol == null)
            {
                return new FindReferencesScopedResult(null, [], 0,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{symbolId}' could not be resolved.",
                        ("symbolId", symbolId),
                        ("operation", operation)));
            }

            if (string.Equals(scope, ReferenceScopes.Document, StringComparison.Ordinal))
            {
                var pathError = _referenceSearchService.TryValidateDocumentPath(path!, solution);
                if (pathError != null)
                {
                    return new FindReferencesScopedResult(null, [], 0, pathError);
                }
            }

            var references = await _referenceSearchService
                .FindReferencesScopedAsync(symbol, solution, scope, path, ownerProject, ct)
                .ConfigureAwait(false);

            return new FindReferencesScopedResult(
                new UsageSymbolSummary(
                    symbol.ToSymbolDescriptor().SymbolId,
                    symbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.MinimallyQualifiedFormat),
                    symbol.Kind.ToString(),
                    CreateOptionalSourceLocation(symbol)),
                GroupReferencesByFile(references),
                references.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindReferencesScoped failed for {SymbolId}", symbolId);
            return new FindReferencesScopedResult(null, [], 0,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to find references '{symbolId}': {ex.Message}",
                    ("symbolId", symbolId),
                    ("operation", operation)));
        }
    }

    private static IReadOnlyList<ReferenceFileGroup> GroupReferencesByFile(IReadOnlyList<SourceLocation> references)
        => references
            .GroupBy(static reference => reference.FilePath, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => new ReferenceFileGroup(
                group.Key,
                group.Select(static reference => new ReferencePosition(reference.Line, reference.Column)).ToArray()))
            .ToArray();

    private static SourceLocation? CreateOptionalSourceLocation(Microsoft.CodeAnalysis.ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static entry => entry.IsInSource);
        return location?.ToSourceLocation();
    }

    private static CompactSymbolSummary MapCompactSymbolSummary(SymbolDescriptor symbol)
        => new(
            symbol.SymbolId,
            symbol.Name,
            symbol.Kind,
            symbol.DeclarationLocation,
            symbol.ContainingType ?? symbol.ContainingNamespace);
}
