namespace RoslynMcp.Tools.Inspection.TraceCallFlow;

public sealed record Request(
    string? SymbolId = null,
    string? Path = null,
    int? Line = null,
    int? Column = null,
    string? Direction = null,
    int? Depth = null,
    bool IncludePossibleTargets = false);

public sealed record Result(
    string? RootSymbolId,
    TraceRootSummary? Root,
    string Direction,
    int Depth,
    IReadOnlyDictionary<string, TraceSymbolEntry>? Symbols,
    IReadOnlyList<TraceFlowEdge> Edges,
    IReadOnlyList<TraceFlowEdge>? PossibleTargetEdges = null,
    IReadOnlyList<FlowTransition>? Transitions = null,
    IReadOnlyList<string>? RootUncertaintyCategories = null,
    ErrorInfo? Error = null);

public sealed record TraceRootSummary(string Name, string Kind, string? Owner, SourceLocation? Location);
public sealed record TraceSymbolEntry(string Display, SourceLocation? Location);
public sealed record TraceFlowEdge(string From, string To, SourceLocation Site, string Kind, IReadOnlyList<string>? UncertaintyCategories = null, IReadOnlyList<string>? RelatedSymbolIds = null);
public sealed record FlowTransition(string FromProject, string ToProject, int Count, IReadOnlyList<string>? UncertaintyCategories = null);
public sealed record SourceLocation(string FilePath, int Line, int Column);
public sealed record ErrorInfo(string Code, string Message, IReadOnlyDictionary<string, string>? Details = null);

internal static class FlowDirections
{
    public const string Upstream = "upstream";
    public const string Downstream = "downstream";
    public const string Both = "both";
}

internal static class FlowEvidenceKinds
{
    public const string DirectStatic = "direct_static";
}
