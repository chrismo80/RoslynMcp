namespace RoslynMcp.Tools.Inspection.FindImplementations;

public sealed record Result(CompactSymbol? Symbol, IReadOnlyList<CompactSymbol> Implementations, ErrorInfo? Error = null);

public sealed record CompactSymbol(
    string SymbolId,
    string Display,
    string Kind,
    SourceLocation? Location,
    string? Owner = null);

public sealed record SourceLocation(string FilePath, int Line, int Column);

public sealed record ErrorInfo(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);
