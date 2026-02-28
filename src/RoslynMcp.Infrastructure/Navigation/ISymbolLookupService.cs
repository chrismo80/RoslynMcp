using RoslynMcp.Core.Models.Navigation;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Navigation;

public interface ISymbolLookupService
{
    Task<ISymbol?> ResolveSymbolAsync(string symbolId, Solution solution, CancellationToken ct);
    Task<(ISymbol? Symbol, Project? OwnerProject)> ResolveSymbolWithProjectAsync(string symbolId, Solution solution, CancellationToken ct);
    Task<ISymbol?> GetSymbolAtPositionAsync(Solution solution, string path, int line, int column, CancellationToken ct);
    Task<(IReadOnlyList<SymbolDescriptor> Symbols, int TotalCount)> SearchSymbolsAsync(
        Solution solution,
        string query,
        int offset,
        int limit,
        CancellationToken ct);
    Task<(IReadOnlyList<SymbolDescriptor> Symbols, int TotalCount)> SearchSymbolsScopedAsync(
        Solution solution,
        string query,
        string scope,
        string? path,
        string? kind,
        string? accessibility,
        int offset,
        int limit,
        CancellationToken ct);
}
