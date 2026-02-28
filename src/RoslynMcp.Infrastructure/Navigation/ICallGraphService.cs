using RoslynMcp.Core.Models.Navigation;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Navigation;

public interface ICallGraphService
{
    bool IsValidDirection(string direction);
    Task<IReadOnlyList<CallEdge>> GetCallersAsync(ISymbol root, Solution solution, int maxDepth, CancellationToken ct);
    Task<IReadOnlyList<CallEdge>> GetCalleesAsync(ISymbol root, Solution solution, int maxDepth, CancellationToken ct);
    Task<IReadOnlyList<CallEdge>> GetCallGraphAsync(ISymbol root, Solution solution, string direction, int maxDepth, CancellationToken ct);
}
