using RoslynMcp.Core.Models.Common;

namespace RoslynMcp.Core.Models.Navigation;

public sealed record SymbolDescriptor(
    string SymbolId,
    string Name,
    string Kind,
    string? ContainingType,
    string? ContainingNamespace,
    SourceLocation DeclarationLocation);

public sealed record FindSymbolRequest(string SymbolId);

public sealed record FindSymbolResult(SymbolDescriptor? Symbol, ErrorInfo? Error = null);

public sealed record GetSymbolAtPositionRequest(string Path, int Line, int Column);

public sealed record GetSymbolAtPositionResult(SymbolDescriptor? Symbol, ErrorInfo? Error = null);

public sealed record SearchSymbolsRequest(string Query, int? Limit = null, int? Offset = null);

public sealed record SearchSymbolsResult(IReadOnlyList<SymbolDescriptor> Symbols, int TotalCount, ErrorInfo? Error = null);

public static class SymbolSearchScopes
{
    public const string Document = "document";
    public const string Project = "project";
    public const string Solution = "solution";
}

public sealed record SearchSymbolsScopedRequest(
    string Query,
    string Scope,
    string? Path = null,
    string? Kind = null,
    string? Accessibility = null,
    int? Limit = null,
    int? Offset = null);

public sealed record SearchSymbolsScopedResult(IReadOnlyList<SymbolDescriptor> Symbols, int TotalCount, ErrorInfo? Error = null);

public sealed record GetSignatureRequest(string SymbolId);

public sealed record GetSignatureResult(SymbolDescriptor? Symbol, string Signature, ErrorInfo? Error = null);

public sealed record FindReferencesRequest(string SymbolId);

public sealed record FindReferencesResult(SymbolDescriptor? Symbol, IReadOnlyList<SourceLocation> References, ErrorInfo? Error = null);

public static class ReferenceScopes
{
    public const string Document = "document";
    public const string Project = "project";
    public const string Solution = "solution";
}

public sealed record FindReferencesScopedRequest(string SymbolId, string Scope, string? Path = null);

public sealed record FindReferencesScopedResult(SymbolDescriptor? Symbol,
    IReadOnlyList<SourceLocation> References,
    int TotalCount,
    ErrorInfo? Error = null);

public sealed record FindImplementationsRequest(string SymbolId);

public sealed record FindImplementationsResult(SymbolDescriptor? Symbol, IReadOnlyList<SymbolDescriptor> Implementations, ErrorInfo? Error = null);

public sealed record GetTypeHierarchyRequest(string SymbolId, bool? IncludeTransitive = null, int? MaxDerived = null);

public sealed record GetTypeHierarchyResult(SymbolDescriptor? Symbol,
    IReadOnlyList<SymbolDescriptor> BaseTypes,
    IReadOnlyList<SymbolDescriptor> ImplementedInterfaces,
    IReadOnlyList<SymbolDescriptor> DerivedTypes,
    ErrorInfo? Error = null);

public sealed record GetSymbolOutlineRequest(string SymbolId, int? Depth = null);

public sealed record SymbolMemberOutline(string Name, string Kind, string Signature, string Accessibility, bool IsStatic);

public sealed record GetSymbolOutlineResult(SymbolDescriptor? Symbol,
    IReadOnlyList<SymbolMemberOutline> Members,
    IReadOnlyList<string> Attributes,
    ErrorInfo? Error = null);

public static class CallGraphDirections
{
    public const string Incoming = "incoming";
    public const string Outgoing = "outgoing";
    public const string Both = "both";
}

public sealed record GetCallersRequest(string SymbolId, int? MaxDepth = null);

public sealed record GetCalleesRequest(string SymbolId, int? MaxDepth = null);

public sealed record CallEdge(string FromSymbolId, string ToSymbolId, SourceLocation Location);

public sealed record GetCallGraphRequest(string SymbolId, string Direction, int? MaxDepth = null);

public sealed record GetCallGraphResult(SymbolDescriptor? RootSymbol,
    IReadOnlyList<CallEdge> Edges,
    int NodeCount,
    int EdgeCount,
    ErrorInfo? Error = null);

public sealed record GetCallersResult(SymbolDescriptor? Symbol, IReadOnlyList<CallEdge> Callers, ErrorInfo? Error = null);

public sealed record GetCalleesResult(SymbolDescriptor? Symbol, IReadOnlyList<CallEdge> Callees, ErrorInfo? Error = null);
