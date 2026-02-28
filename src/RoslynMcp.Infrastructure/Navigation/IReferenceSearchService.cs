using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Navigation;

public interface IReferenceSearchService
{
    bool IsValidScope(string scope);
    ErrorInfo? TryValidateDocumentPath(string path, Solution solution);
    Task<IReadOnlyList<SourceLocation>> FindReferencesAsync(ISymbol symbol, Solution solution, CancellationToken ct);
    Task<IReadOnlyList<SourceLocation>> FindReferencesScopedAsync(
        ISymbol symbol,
        Solution solution,
        string scope,
        string? path,
        Project? ownerProject,
        CancellationToken ct);
    Task<IReadOnlyList<SymbolDescriptor>> FindImplementationsAsync(ISymbol symbol, Solution solution, CancellationToken ct);
}
