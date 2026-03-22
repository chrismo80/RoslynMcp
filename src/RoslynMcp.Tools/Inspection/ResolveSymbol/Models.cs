namespace RoslynMcp.Tools.Inspection.ResolveSymbol;

public sealed record Request(
    string? SymbolId = null,
    string? Path = null,
    int? Line = null,
    int? Column = null,
    string? QualifiedName = null,
    string? ProjectPath = null,
    string? ProjectName = null,
    string? ProjectId = null);

public sealed record Result(
    ResolvedSymbol? Symbol,
    bool IsAmbiguous,
    IReadOnlyList<Candidate> Candidates,
    ErrorInfo? Error = null);

public sealed record ResolvedSymbol(
    string SymbolId,
    string DisplayName,
    string Kind,
    SourceLocation? Location,
    string? QualifiedDisplayName = null);

public sealed record Candidate(
    string SymbolId,
    string DisplayName,
    string Kind,
    SourceLocation? Location,
    string ProjectName,
    string? QualifiedDisplayName = null);

public sealed record SourceLocation(string FilePath, int Line, int Column);

public sealed record ErrorInfo(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);

internal readonly record struct QualifiedNameSegment(string Name, int? GenericArity)
{
    public bool Matches(string actualName, int? actualGenericArity)
    {
        if (!string.Equals(Name, actualName, StringComparison.Ordinal))
            return false;

        return GenericArity is null || GenericArity == actualGenericArity;
    }
}
