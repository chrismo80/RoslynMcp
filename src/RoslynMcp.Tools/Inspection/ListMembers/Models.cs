namespace RoslynMcp.Tools.Inspection.ListMembers;

public sealed record Request(
    string? TypeSymbolId = null,
    string? Path = null,
    int? Line = null,
    int? Column = null,
    string? Kind = null,
    string? Accessibility = null,
    string? Binding = null,
    bool IncludeInherited = false,
    int? Limit = null,
    int? Offset = null);

public sealed record Result(
    IReadOnlyList<Entry> Members,
    int TotalCount,
    bool IncludeInherited,
    ErrorInfo? Error = null);

public sealed record Entry(
    string DisplayName,
    string SymbolId,
    string Kind,
    string Signature,
    SourceLocation? Location,
    string Accessibility,
    bool IsStatic);

public sealed record SourceLocation(string FilePath, int Line, int Column);

public sealed record ErrorInfo(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);
