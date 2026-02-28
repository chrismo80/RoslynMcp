using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Infrastructure.Navigation;

public sealed class RoslynNavigationService : INavigationService
{
    private const int DefaultMaxDerived = 200;
    private const int MaxCallGraphDepth = 4;
    private const int MaxOutlineDepth = 3;

    private readonly IRoslynSolutionAccessor _solutionAccessor;
    private readonly ISymbolLookupService _symbolLookupService;
    private readonly IReferenceSearchService _referenceSearchService;
    private readonly ICallGraphService _callGraphService;
    private readonly ITypeIntrospectionService _typeIntrospectionService;
    private readonly ILogger<RoslynNavigationService> _logger;

    public RoslynNavigationService(IRoslynSolutionAccessor solutionAccessor,
        ISymbolLookupService symbolLookupService,
        IReferenceSearchService referenceSearchService,
        ICallGraphService callGraphService,
        ITypeIntrospectionService typeIntrospectionService,
        ILogger<RoslynNavigationService>? logger = null)
    {
        _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));
        _symbolLookupService = symbolLookupService ?? throw new ArgumentNullException(nameof(symbolLookupService));
        _referenceSearchService = referenceSearchService ?? throw new ArgumentNullException(nameof(referenceSearchService));
        _callGraphService = callGraphService ?? throw new ArgumentNullException(nameof(callGraphService));
        _typeIntrospectionService = typeIntrospectionService ?? throw new ArgumentNullException(nameof(typeIntrospectionService));
        _logger = logger ?? NullLogger<RoslynNavigationService>.Instance;
    }

    public async Task<FindSymbolResult> FindSymbolAsync(FindSymbolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "find-symbol");
        if (invalidInputError != null)
        {
            return new FindSymbolResult(null, invalidInputError);
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new FindSymbolResult(null, error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new FindSymbolResult(null,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "find-symbol")));
            }

            return new FindSymbolResult(NavigationModelUtilities.CreateDescriptor(symbol));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindSymbol failed for {SymbolId}", request.SymbolId);
            return new FindSymbolResult(null,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to resolve symbol '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "find-symbol")));
        }
    }

    public async Task<GetSymbolAtPositionResult> GetSymbolAtPositionAsync(GetSymbolAtPositionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Path) || request.Line <= 0 || request.Column <= 0)
        {
            return new GetSymbolAtPositionResult(null,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "path, line and column must be provided.",
                    ("operation", "get_symbol_at_position")));
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetSymbolAtPositionResult(null, error);
            }

            var symbol = await _symbolLookupService
                .GetSymbolAtPositionAsync(solution, request.Path, request.Line, request.Column, ct)
                .ConfigureAwait(false);

            if (symbol == null)
            {
                return new GetSymbolAtPositionResult(null,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        "No symbol could be resolved at the specified position.",
                        ("path", request.Path),
                        ("line", request.Line.ToString()),
                        ("column", request.Column.ToString()),
                        ("operation", "get_symbol_at_position")));
            }

            return new GetSymbolAtPositionResult(NavigationModelUtilities.CreateDescriptor(symbol));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSymbolAtPosition failed for {Path}:{Line}:{Column}", request.Path, request.Line, request.Column);
            return new GetSymbolAtPositionResult(null,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to resolve symbol at position: {ex.Message}",
                    ("operation", "get_symbol_at_position")));
        }
    }

    public async Task<SearchSymbolsResult> SearchSymbolsAsync(SearchSymbolsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new SearchSymbolsResult(Array.Empty<SymbolDescriptor>(), 0, error);
            }

            var query = request.Query?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                return new SearchSymbolsResult(Array.Empty<SymbolDescriptor>(), 0);
            }

            var limit = Math.Max(request.Limit ?? int.MaxValue, 0);
            var offset = Math.Max(request.Offset ?? 0, 0);
            var (symbols, total) = await _symbolLookupService.SearchSymbolsAsync(solution, query, offset, limit, ct).ConfigureAwait(false);
            return new SearchSymbolsResult(symbols, total);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchSymbols failed for {Query}", request.Query);
            return new SearchSymbolsResult(Array.Empty<SymbolDescriptor>(), 0,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Search failed: {ex.Message}",
                    ("query", request.Query),
                    ("operation", "search-symbols")));
        }
    }

    public async Task<SearchSymbolsScopedResult> SearchSymbolsScopedAsync(SearchSymbolsScopedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(), 0);
        }

        if (!IsValidSearchScope(request.Scope))
        {
            return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "scope must be one of: document, project, solution.",
                    ("parameter", "scope"),
                    ("operation", "search_symbols_scoped")));
        }

        if (string.Equals(request.Scope, SymbolSearchScopes.Document, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(request.Path))
        {
            return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is document.",
                    ("parameter", "path"),
                    ("operation", "search_symbols_scoped")));
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(), 0, error);
            }

            if (!PathExistsInScope(solution, request.Scope, request.Path))
            {
                return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(),
                    0,
                    NavigationErrorFactory.CreateError(ErrorCodes.PathOutOfScope,
                        "The provided path is outside the selected solution scope.",
                        ("path", request.Path),
                        ("operation", "search_symbols_scoped")));
            }

            var limit = Math.Max(request.Limit ?? int.MaxValue, 0);
            var offset = Math.Max(request.Offset ?? 0, 0);
            var (symbols, total) = await _symbolLookupService
                .SearchSymbolsScopedAsync(solution,
                    request.Query,
                    request.Scope,
                    request.Path,
                    request.Kind,
                    request.Accessibility,
                    offset,
                    limit,
                    ct)
                .ConfigureAwait(false);

            return new SearchSymbolsScopedResult(symbols, total);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchSymbolsScoped failed for {Query}", request.Query);
            return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Scoped search failed: {ex.Message}",
                    ("operation", "search_symbols_scoped")));
        }
    }

    public async Task<GetSignatureResult> GetSignatureAsync(GetSignatureRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get_signature");
        if (invalidInputError != null)
        {
            return new GetSignatureResult(null, string.Empty, invalidInputError);
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetSignatureResult(null, string.Empty, error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetSignatureResult(null,
                    string.Empty,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get_signature")));
            }

            var signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return new GetSignatureResult(NavigationModelUtilities.CreateDescriptor(symbol), signature);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSignature failed for {SymbolId}", request.SymbolId);
            return new GetSignatureResult(null,
                string.Empty,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to build signature '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get_signature")));
        }
    }

    public async Task<FindReferencesResult> FindReferencesAsync(FindReferencesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "find-references");
        if (invalidInputError != null)
        {
            return new FindReferencesResult(null, Array.Empty<SourceLocation>(), invalidInputError);
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new FindReferencesResult(null, Array.Empty<SourceLocation>(), error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new FindReferencesResult(null,
                    Array.Empty<SourceLocation>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "find-references")));
            }

            var references = await _referenceSearchService.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
            return new FindReferencesResult(NavigationModelUtilities.CreateDescriptor(symbol), references);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindReferences failed for {SymbolId}", request.SymbolId);
            return new FindReferencesResult(null,
                Array.Empty<SourceLocation>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to find references '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "find-references")));
        }
    }

    public async Task<FindReferencesScopedResult> FindReferencesScopedAsync(FindReferencesScopedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "find-references-scoped");
        if (invalidSymbolIdError != null)
        {
            return new FindReferencesScopedResult(null, Array.Empty<SourceLocation>(), 0, invalidSymbolIdError);
        }

        if (!_referenceSearchService.IsValidScope(request.Scope))
        {
            return new FindReferencesScopedResult(null,
                Array.Empty<SourceLocation>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "scope must be one of: document, project, solution.",
                    ("parameter", "scope"),
                    ("operation", "find-references-scoped")));
        }

        if (string.Equals(request.Scope, ReferenceScopes.Document, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(request.Path))
        {
            return new FindReferencesScopedResult(null,
                Array.Empty<SourceLocation>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is document.",
                    ("parameter", "path"),
                    ("operation", "find-references-scoped")));
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new FindReferencesScopedResult(null, Array.Empty<SourceLocation>(), 0, error);
            }

            var (symbol, ownerProject) = await _symbolLookupService.ResolveSymbolWithProjectAsync(request.SymbolId, solution, ct)
                .ConfigureAwait(false);
            if (symbol == null)
            {
                return new FindReferencesScopedResult(null,
                    Array.Empty<SourceLocation>(),
                    0,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "find-references-scoped")));
            }

            if (string.Equals(request.Scope, ReferenceScopes.Document, StringComparison.Ordinal))
            {
                var pathError = _referenceSearchService.TryValidateDocumentPath(request.Path!, solution);
                if (pathError != null)
                {
                    return new FindReferencesScopedResult(null, Array.Empty<SourceLocation>(), 0, pathError);
                }
            }

            var references = await _referenceSearchService
                .FindReferencesScopedAsync(symbol, solution, request.Scope, request.Path, ownerProject, ct)
                .ConfigureAwait(false);

            return new FindReferencesScopedResult(
                NavigationModelUtilities.CreateDescriptor(symbol),
                references,
                references.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindReferencesScoped failed for {SymbolId}", request.SymbolId);
            return new FindReferencesScopedResult(null,
                Array.Empty<SourceLocation>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to find scoped references '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "find-references-scoped")));
        }
    }

    public async Task<FindImplementationsResult> FindImplementationsAsync(FindImplementationsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "find-implementations");
        if (invalidInputError != null)
        {
            return new FindImplementationsResult(null, Array.Empty<SymbolDescriptor>(), invalidInputError);
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new FindImplementationsResult(null, Array.Empty<SymbolDescriptor>(), error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new FindImplementationsResult(null,
                    Array.Empty<SymbolDescriptor>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "find-implementations")));
            }

            var implementations = await _referenceSearchService.FindImplementationsAsync(symbol, solution, ct).ConfigureAwait(false);
            return new FindImplementationsResult(NavigationModelUtilities.CreateDescriptor(symbol), implementations);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindImplementations failed for {SymbolId}", request.SymbolId);
            return new FindImplementationsResult(null,
                Array.Empty<SymbolDescriptor>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to find implementations '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "find-implementations")));
        }
    }

    public async Task<GetTypeHierarchyResult> GetTypeHierarchyAsync(GetTypeHierarchyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-type-hierarchy");
        if (invalidSymbolIdError != null)
        {
            return new GetTypeHierarchyResult(null,
                Array.Empty<SymbolDescriptor>(),
                Array.Empty<SymbolDescriptor>(),
                Array.Empty<SymbolDescriptor>(),
                invalidSymbolIdError);
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetTypeHierarchyResult(null,
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetTypeHierarchyResult(null,
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-type-hierarchy")));
            }

            var typeSymbol = _typeIntrospectionService.GetRelatedType(symbol);
            if (typeSymbol == null)
            {
                return new GetTypeHierarchyResult(null,
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
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
                NavigationModelUtilities.CreateDescriptor(typeSymbol),
                baseTypes,
                interfaces,
                derived);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTypeHierarchy failed for {SymbolId}", request.SymbolId);
            return new GetTypeHierarchyResult(null,
                Array.Empty<SymbolDescriptor>(),
                Array.Empty<SymbolDescriptor>(),
                Array.Empty<SymbolDescriptor>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to compute type hierarchy '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-type-hierarchy")));
        }
    }

    public async Task<GetSymbolOutlineResult> GetSymbolOutlineAsync(GetSymbolOutlineRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-symbol-outline");
        if (invalidSymbolIdError != null)
        {
            return new GetSymbolOutlineResult(null, Array.Empty<SymbolMemberOutline>(), Array.Empty<string>(), invalidSymbolIdError);
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetSymbolOutlineResult(null, Array.Empty<SymbolMemberOutline>(), Array.Empty<string>(), error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetSymbolOutlineResult(null,
                    Array.Empty<SymbolMemberOutline>(),
                    Array.Empty<string>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-symbol-outline")));
            }

            var depth = Math.Clamp(request.Depth ?? 1, 1, MaxOutlineDepth);
            var members = _typeIntrospectionService.CollectOutlineMembers(symbol, depth);
            var attributes = _typeIntrospectionService.CollectAttributes(symbol);
            return new GetSymbolOutlineResult(NavigationModelUtilities.CreateDescriptor(symbol), members, attributes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSymbolOutline failed for {SymbolId}", request.SymbolId);
            return new GetSymbolOutlineResult(null,
                Array.Empty<SymbolMemberOutline>(),
                Array.Empty<string>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to build symbol outline '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-symbol-outline")));
        }
    }

    public async Task<GetCallersResult> GetCallersAsync(GetCallersRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-callers");
        if (invalidInputError != null)
        {
            return new GetCallersResult(null, Array.Empty<CallEdge>(), invalidInputError);
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetCallersResult(null, Array.Empty<CallEdge>(), error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetCallersResult(null,
                    Array.Empty<CallEdge>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-callers")));
            }

            var depth = Math.Max(request.MaxDepth ?? 1, 1);
            var edges = await _callGraphService.GetCallersAsync(symbol, solution, depth, ct).ConfigureAwait(false);
            return new GetCallersResult(NavigationModelUtilities.CreateDescriptor(symbol), edges);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCallers failed for {SymbolId}", request.SymbolId);
            return new GetCallersResult(null,
                Array.Empty<CallEdge>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to compute callers '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-callers")));
        }
    }

    public async Task<GetCalleesResult> GetCalleesAsync(GetCalleesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-callees");
        if (invalidInputError != null)
        {
            return new GetCalleesResult(null, Array.Empty<CallEdge>(), invalidInputError);
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetCalleesResult(null, Array.Empty<CallEdge>(), error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetCalleesResult(null,
                    Array.Empty<CallEdge>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-callees")));
            }

            var depth = Math.Max(request.MaxDepth ?? 1, 1);
            var edges = await _callGraphService.GetCalleesAsync(symbol, solution, depth, ct).ConfigureAwait(false);
            return new GetCalleesResult(NavigationModelUtilities.CreateDescriptor(symbol), edges);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCallees failed for {SymbolId}", request.SymbolId);
            return new GetCalleesResult(null,
                Array.Empty<CallEdge>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to compute callees '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-callees")));
        }
    }

    public async Task<GetCallGraphResult> GetCallGraphAsync(GetCallGraphRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-callgraph");
        if (invalidSymbolIdError != null)
        {
            return new GetCallGraphResult(null, Array.Empty<CallEdge>(), 0, 0, invalidSymbolIdError);
        }

        if (!_callGraphService.IsValidDirection(request.Direction))
        {
            return new GetCallGraphResult(null,
                Array.Empty<CallEdge>(),
                0,
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "direction must be one of: incoming, outgoing, both.",
                    ("parameter", "direction"),
                    ("operation", "get-callgraph")));
        }

        try
        {
            var (solution, error) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetCallGraphResult(null, Array.Empty<CallEdge>(), 0, 0, error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetCallGraphResult(null,
                    Array.Empty<CallEdge>(),
                    0,
                    0,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-callgraph")));
            }

            var maxDepth = Math.Clamp(request.MaxDepth ?? 1, 1, MaxCallGraphDepth);
            var orderedEdges = await _callGraphService
                .GetCallGraphAsync(symbol, solution, request.Direction, maxDepth, ct)
                .ConfigureAwait(false);

            var nodes = new HashSet<string>(StringComparer.Ordinal)
            {
                SymbolIdentity.CreateId(symbol.OriginalDefinition ?? symbol)
            };

            foreach (var edge in orderedEdges)
            {
                nodes.Add(edge.FromSymbolId);
                nodes.Add(edge.ToSymbolId);
            }

            return new GetCallGraphResult(
                NavigationModelUtilities.CreateDescriptor(symbol),
                orderedEdges,
                nodes.Count,
                orderedEdges.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCallGraph failed for {SymbolId}", request.SymbolId);
            return new GetCallGraphResult(null,
                Array.Empty<CallEdge>(),
                0,
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to build call graph '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-callgraph")));
        }
    }

    private async Task<(Solution? Solution, ErrorInfo? Error)> TryGetSolutionAsync(CancellationToken ct)
    {
        try
        {
            return await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to access solution state");
            return (null,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    "Unable to access the current solution.",
                    ("operation", "navigation")));
        }
    }

    private static bool IsValidSearchScope(string scope)
        => string.Equals(scope, SymbolSearchScopes.Document, StringComparison.Ordinal)
           || string.Equals(scope, SymbolSearchScopes.Project, StringComparison.Ordinal)
           || string.Equals(scope, SymbolSearchScopes.Solution, StringComparison.Ordinal);

    private static bool PathExistsInScope(Solution solution, string scope, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(scope, SymbolSearchScopes.Solution, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(scope, SymbolSearchScopes.Document, StringComparison.Ordinal))
        {
            return solution.Projects
                .SelectMany(static p => p.Documents)
                .Any(document => NavigationModelUtilities.MatchesByNormalizedPath(document.FilePath, path));
        }

        return solution.Projects.Any(project =>
            NavigationModelUtilities.MatchesByNormalizedPath(project.FilePath, path)
            || string.Equals(project.Name, path, StringComparison.OrdinalIgnoreCase));
    }
}
