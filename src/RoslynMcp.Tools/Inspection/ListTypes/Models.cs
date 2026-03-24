namespace RoslynMcp.Tools.Inspection.ListTypes;

public sealed record Result(
    IReadOnlyList<Entry> Types,
    ErrorInfo? Error = null);

public sealed record Entry(
    TypeSymbol? Type = null,
    IReadOnlyList<string>? Members = null);