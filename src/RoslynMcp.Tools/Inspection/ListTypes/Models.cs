namespace RoslynMcp.Tools.Inspection.ListTypes;

public sealed record Result(
    IReadOnlyList<Entry> Types,
    ErrorInfo? Error = null);

public sealed record Entry(
    string DisplayName,
    string SymbolId,
    Location? Location,
    string Kind,
    int? Arity,
    string? Summary = null,
    IReadOnlyList<string>? Members = null);

public sealed record ErrorInfo(
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);
