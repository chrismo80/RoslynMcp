namespace RoslynMcp.Tools.Inspection.FindCallers;

public sealed record Result(
    MemberSymbol? Root,
    IReadOnlyList<MemberSymbol> Callers,
    ErrorInfo? Error = null);