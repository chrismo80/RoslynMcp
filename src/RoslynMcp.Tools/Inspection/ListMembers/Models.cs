namespace RoslynMcp.Tools.Inspection.ListMembers;

public sealed record Result(
    IReadOnlyList<MemberSymbol> Members,
    int Count,
    ErrorInfo? Error = null);