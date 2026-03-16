using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Infrastructure.Navigation;

/// <summary>
/// Resolves type hierarchies: base types, derived types, and implementing types.
/// Provides type introspection for inheritance analysis.
/// </summary>
internal sealed class NavigationTypeHierarchyService(
    NavigationSolutionProvider solutionProvider,
    ISymbolLookupService symbolLookupService,
    ITypeIntrospectionService typeIntrospectionService,
    ILogger logger)
{
    private const int DefaultMaxDerived = 200;
    private const int MaxOutlineDepth = 3;

    private readonly NavigationSolutionProvider _solutionProvider = solutionProvider ?? throw new ArgumentNullException(nameof(solutionProvider));
    private readonly ISymbolLookupService _symbolLookupService = symbolLookupService ?? throw new ArgumentNullException(nameof(symbolLookupService));
    private readonly ITypeIntrospectionService _typeIntrospectionService = typeIntrospectionService ?? throw new ArgumentNullException(nameof(typeIntrospectionService));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<GetTypeHierarchyResult> GetTypeHierarchyAsync(GetTypeHierarchyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-type-hierarchy");
        if (invalidSymbolIdError != null)
        {
            return new GetTypeHierarchyResult(null, [], [], [], invalidSymbolIdError);
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetTypeHierarchyResult(null, [], [], [], error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetTypeHierarchyResult(null, [], [], [],
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-type-hierarchy")));
            }

            var typeSymbol = _typeIntrospectionService.GetRelatedType(symbol);
            if (typeSymbol == null)
            {
                return new GetTypeHierarchyResult(null,
                    Array.Empty<CompactSymbolSummary>(),
                    Array.Empty<CompactSymbolSummary>(),
                    Array.Empty<CompactSymbolSummary>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                        "symbolId must resolve to a type or a member declared on a type.",
                        ("parameter", "symbolId"),
                        ("operation", "get-type-hierarchy")));
            }

            var includeTransitive = request.IncludeTransitive ?? true;
            var maxDerived = Math.Max(request.MaxDerived ?? DefaultMaxDerived, 0);
            var baseTypes = _typeIntrospectionService.CollectBaseTypes(typeSymbol, includeTransitive);
            var interfaces = _typeIntrospectionService.CollectImplementedInterfaces(typeSymbol, includeTransitive);
            var derived = await _typeIntrospectionService
                .CollectDerivedTypesAsync(typeSymbol, solution, includeTransitive, maxDerived, ct)
                .ConfigureAwait(false);

            return new GetTypeHierarchyResult(
                typeSymbol.ToCompactSymbolSummary(),
                baseTypes.Select(MapCompactSummary).ToArray(),
                interfaces.Select(MapCompactSummary).ToArray(),
                derived.Select(MapCompactSummary).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTypeHierarchy failed for {SymbolId}", request.SymbolId);
            return new GetTypeHierarchyResult(null,
                Array.Empty<CompactSymbolSummary>(),
                Array.Empty<CompactSymbolSummary>(),
                Array.Empty<CompactSymbolSummary>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to compute type hierarchy '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-type-hierarchy")));
        }
    }

    private static CompactSymbolSummary MapCompactSummary(SymbolDescriptor symbol)
        => new(
            symbol.SymbolId,
            symbol.Name,
            symbol.Kind,
            symbol.DeclarationLocation,
            symbol.ContainingType ?? symbol.ContainingNamespace);

    public async Task<GetSymbolOutlineResult> GetSymbolOutlineAsync(GetSymbolOutlineRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-symbol-outline");
        if (invalidSymbolIdError != null)
        {
            return new GetSymbolOutlineResult(null, [], [], invalidSymbolIdError);
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetSymbolOutlineResult(null, Array.Empty<SymbolMemberOutline>(), Array.Empty<string>(), error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetSymbolOutlineResult(null, [], [],
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-symbol-outline")));
            }

            var depth = Math.Clamp(request.Depth ?? 1, 1, MaxOutlineDepth);
            var members = _typeIntrospectionService.CollectOutlineMembers(symbol, depth);
            var attributes = _typeIntrospectionService.CollectAttributes(symbol);
            return new GetSymbolOutlineResult(symbol.ToSymbolDescriptor(), members, attributes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSymbolOutline failed for {SymbolId}", request.SymbolId);
            return new GetSymbolOutlineResult(null, [], [],
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to build symbol outline '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-symbol-outline")));
        }
    }
}
