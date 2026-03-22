using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Inspection.ResolveSymbols;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddResolveSymbolsTool() => services
            .AddSingleton<Service>()
            .AddSingleton<Tool>();
    }

    internal static ResolveSymbol.Request ToResolveSymbolRequest(this Entry entry)
        => new(
            entry.SymbolId,
            entry.Path,
            entry.Line,
            entry.Column,
            entry.QualifiedName,
            entry.ProjectPath,
            entry.ProjectName,
            entry.ProjectId);

    internal static ItemResult ToItemResult(this ResolveSymbol.Result result, int index, string? label)
        => new(
            index,
            label,
            result.Symbol.ToResolvedSymbol(),
            result.IsAmbiguous,
            [.. result.Candidates.Select(static candidate => candidate.ToCandidate())],
            result.Error.ToErrorInfo());

    private static ResolvedSymbol? ToResolvedSymbol(this ResolveSymbol.ResolvedSymbol? symbol)
        => symbol is null ? null : new ResolvedSymbol(symbol.SymbolId, symbol.DisplayName, symbol.Kind, symbol.Location.ToSourceLocation(), symbol.QualifiedDisplayName);

    private static Candidate ToCandidate(this ResolveSymbol.Candidate candidate)
        => new(candidate.SymbolId, candidate.DisplayName, candidate.Kind, candidate.Location.ToSourceLocation(), candidate.ProjectName, candidate.QualifiedDisplayName);

    private static SourceLocation? ToSourceLocation(this ResolveSymbol.SourceLocation? location)
        => location is null ? null : new SourceLocation(location.FilePath, location.Line, location.Column);

    private static ErrorInfo? ToErrorInfo(this ResolveSymbol.ErrorInfo? error)
        => error is null ? null : new ErrorInfo(error.Code, error.Message, error.Details);
}
