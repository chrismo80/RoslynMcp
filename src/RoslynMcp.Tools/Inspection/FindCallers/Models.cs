namespace RoslynMcp.Tools.Inspection.FindCallers;

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

public sealed record TraceFlowEdge(
    string From,
    string To,
    Location Site,
    string Kind,
    IReadOnlyList<string>? UncertaintyCategories = null,
    IReadOnlyList<string>? RelatedSymbolIds = null);
    
public sealed record TraceRootSummary(
    string Name,
    string Kind,
    string? Owner,
    Location? Location);
    
public sealed record FlowTransition(
    string FromProject,
    string ToProject,
    int Count,
    IReadOnlyList<string>? UncertaintyCategories = null);
    
public sealed record TraceSymbolEntry(
    string Display,
    Location? Location);