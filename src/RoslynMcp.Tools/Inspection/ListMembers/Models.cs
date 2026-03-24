namespace RoslynMcp.Tools.Inspection.ListMembers;

public sealed record MemberEntry(
    string DisplayName,
    string SymbolId,
    string Kind,
    string Signature,
    Location? Location,
    string Accessibility,
    bool IsStatic);

public sealed record Result(
    IReadOnlyList<MemberEntry> Members,
    int Count,
    ErrorInfo? Error = null);