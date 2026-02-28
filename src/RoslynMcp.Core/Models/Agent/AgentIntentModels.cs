using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Core.Models.Refactoring;

namespace RoslynMcp.Core.Models.Agent;

public enum RiskLevel
{
    Low,
    Medium,
    High
}

public sealed record LoadSolutionRequest(string? SolutionHintPath = null);

public sealed record ProjectSummary(string Name, string? Path);

public sealed record DiagnosticsSummary(int ErrorCount, int WarningCount, int InfoCount, int TotalCount);

public sealed record LoadSolutionResult(
    string? SelectedSolutionPath,
    string WorkspaceId,
    string WorkspaceSnapshotId,
    IReadOnlyList<ProjectSummary> Projects,
    DiagnosticsSummary BaselineDiagnostics,
    ErrorInfo? Error = null);

public sealed record UnderstandCodebaseRequest(string? Profile = null);

public sealed record ModuleSummary(string Name, string? Path, int OutgoingDependencies, int IncomingDependencies);

public sealed record ProjectDependency(string ProjectName, string ProjectId);

public sealed record ListDependenciesRequest(
    string? ProjectPath = null,
    string? ProjectName = null,
    string? ProjectId = null,
    string? Direction = null); // "outgoing", "incoming", "both" (default)

public sealed record ListDependenciesResult(
    IReadOnlyList<ProjectDependency> Dependencies,
    int TotalCount,
    ErrorInfo? Error = null);

public sealed record HotspotSummary(
    string Label,
    string Path,
    string Reason,
    int Score,
    string SymbolId,
    string DisplayName,
    string FilePath,
    int? StartLine,
    int? EndLine,
    int Complexity,
    int LineCount);

public sealed record ListTypesRequest(
    string? ProjectPath = null,
    string? ProjectName = null,
    string? ProjectId = null,
    string? NamespacePrefix = null,
    string? Kind = null,
    string? Accessibility = null,
    int? Limit = null,
    int? Offset = null);

public sealed record TypeListEntry(
    string DisplayName,
    string SymbolId,
    string FilePath,
    int? Line,
    int? Column,
    string Kind,
    bool IsPartial,
    int? Arity);

public sealed record ListTypesResult(
    IReadOnlyList<TypeListEntry> Types,
    int TotalCount,
    ErrorInfo? Error = null);

public sealed record ListMembersRequest(
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

public sealed record MemberListEntry(
    string DisplayName,
    string SymbolId,
    string Kind,
    string Signature,
    string FilePath,
    int? Line,
    int? Column,
    string Accessibility,
    bool IsStatic);

public sealed record ListMembersResult(
    IReadOnlyList<MemberListEntry> Members,
    int TotalCount,
    bool IncludeInherited,
    ErrorInfo? Error = null);

public sealed record ResolveSymbolRequest(
    string? SymbolId = null,
    string? Path = null,
    int? Line = null,
    int? Column = null,
    string? QualifiedName = null,
    string? ProjectPath = null,
    string? ProjectName = null,
    string? ProjectId = null);

public sealed record ResolvedSymbolSummary(
    string SymbolId,
    string DisplayName,
    string Kind,
    string FilePath,
    int? Line,
    int? Column);

public sealed record ResolveSymbolCandidate(
    string SymbolId,
    string DisplayName,
    string Kind,
    string FilePath,
    int? Line,
    int? Column,
    string ProjectName);

public sealed record ResolveSymbolResult(
    ResolvedSymbolSummary? Symbol,
    bool IsAmbiguous,
    IReadOnlyList<ResolveSymbolCandidate> Candidates,
    ErrorInfo? Error = null);

public sealed record UnderstandCodebaseResult(
    string Profile,
    IReadOnlyList<ModuleSummary> Modules,
    IReadOnlyList<HotspotSummary> Hotspots,
    ErrorInfo? Error = null);

public sealed record ExplainSymbolRequest(string? SymbolId = null, string? Path = null, int? Line = null, int? Column = null);

public sealed record ImpactHint(string Zone, string Reason, int ReferenceCount);

public sealed record ExplainSymbolResult(
    SymbolDescriptor? Symbol,
    string RoleSummary,
    string Signature,
    IReadOnlyList<string> KeyReferences,
    IReadOnlyList<ImpactHint> ImpactHints,
    ErrorInfo? Error = null);

public sealed record FlowTransition(string FromProject, string ToProject, int Count);

public sealed record TraceFlowRequest(
    string? SymbolId = null,
    string? Path = null,
    int? Line = null,
    int? Column = null,
    string? Direction = null,
    int? Depth = null);

public sealed record TraceFlowResult(
    SymbolDescriptor? RootSymbol,
    string Direction,
    int Depth,
    IReadOnlyList<CallEdge> Edges,
    IReadOnlyList<FlowTransition> Transitions,
    ErrorInfo? Error = null);

public sealed record FindCodeSmellsRequest(
    string Path);

public sealed record CodeSmellMatch(
    string Title,
    string Category,
    SourceLocation Location,
    string Origin,
    string RiskLevel);

public sealed record FindCodeSmellsResult(
    IReadOnlyList<CodeSmellMatch> Actions,
    IReadOnlyList<string> Warnings,
    ErrorInfo? Error = null);