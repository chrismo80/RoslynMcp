namespace RoslynMcp.Core.Models;

public enum RiskLevel
{
    Low,
    Medium,
    High
}

public static class SourceBiases
{
    public const string Handwritten = "handwritten";
    public const string Generated = "generated";
    public const string Mixed = "mixed";
    public const string Unknown = "unknown";
}

public static class ResultCompletenessStates
{
    public const string Complete = "complete";
    public const string Partial = "partial";
    public const string Degraded = "degraded";
}

public static class WorkspaceReadinessStates
{
    public const string Ready = "ready";
    public const string DegradedMissingArtifacts = "degraded_missing_artifacts";
    public const string DegradedRestoreRecommended = "degraded_restore_recommended";
}

public sealed record ResultContextMetadata(
    string SourceBias,
    string ResultCompleteness,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> DegradedReasons,
    string? RecommendedNextStep = null);

public sealed record WorkspaceReadiness(
    string State,
    IReadOnlyList<string> DegradedReasons,
    string? RecommendedNextStep = null);

public sealed record LoadSolutionRequest(string? SolutionHintPath = null);

public sealed record ProjectSummary(string Name, string? Path);

public sealed record DiagnosticsSummary(int ErrorCount, int WarningCount, int InfoCount, int TotalCount);

public sealed record LoadSolutionResult(
    string? SelectedSolutionPath,
    string WorkspaceId,
    string WorkspaceSnapshotId,
    IReadOnlyList<ProjectSummary> Projects,
    DiagnosticsSummary BaselineDiagnostics,
    WorkspaceReadiness Readiness,
    ErrorInfo? Error = null);

public sealed record UnderstandCodebaseRequest(string? Profile = null);

public sealed record ModuleSummary(string Name, string? Path, int OutgoingDependencies, int IncomingDependencies);

public sealed record ProjectDependency(string ProjectPath, string? ProjectName = null);

public sealed record ProjectDependencyEdge(string FromProjectPath, string ToProjectPath);

public sealed record ListDependenciesRequest(
    string? ProjectPath = null,
    string? ProjectName = null,
    string? ProjectId = null,
    string? Direction = null); // "outgoing", "incoming", "both" (default)

public sealed record ListDependenciesResult(
    IReadOnlyList<ProjectDependency> Dependencies,
    int TotalCount,
    ErrorInfo? Error = null,
    IReadOnlyList<ProjectDependencyEdge>? Edges = null);

public sealed record HotspotSummary(
    string Display,
    string Reason,
    int Score,
    string SymbolId,
    SourceLocation? Location,
    int Complexity,
    int LineCount);

public sealed record ListTypesRequest(
    string? ProjectPath = null,
    string? ProjectName = null,
    string? ProjectId = null,
    string? NamespacePrefix = null,
    string? Kind = null,
    string? Accessibility = null,
    bool IncludeSummary = false,
    bool IncludeMembers = false,
    int? Limit = null,
    int? Offset = null);

public sealed record TypeListEntry(
    string DisplayName,
    string SymbolId,
    SourceLocation? Location,
    string Kind,
    bool IsPartial,
    int? Arity,
    string? Summary = null,
    IReadOnlyList<string>? Members = null);

public sealed record ListTypesResult(
    IReadOnlyList<TypeListEntry> Types,
    int TotalCount,
    ResultContextMetadata Context,
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
    SourceLocation? Location,
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

public sealed record ResolveSymbolBatchEntry(
    string? SymbolId = null,
    string? Path = null,
    int? Line = null,
    int? Column = null,
    string? QualifiedName = null,
    string? ProjectPath = null,
    string? ProjectName = null,
    string? ProjectId = null,
    string? Label = null);

public sealed record ResolvedSymbolSummary(
    string SymbolId,
    string DisplayName,
    string Kind,
    SourceLocation? Location,
    string? QualifiedDisplayName = null);

public sealed record ResolveSymbolCandidate(
    string SymbolId,
    string DisplayName,
    string Kind,
    SourceLocation? Location,
    string ProjectName,
    string? QualifiedDisplayName = null);

public sealed record ResolveSymbolResult(
    ResolvedSymbolSummary? Symbol,
    bool IsAmbiguous,
    IReadOnlyList<ResolveSymbolCandidate> Candidates,
    ErrorInfo? Error = null);

public sealed record ResolveSymbolsBatchRequest(IReadOnlyList<ResolveSymbolBatchEntry> Entries);

public sealed record ResolveSymbolsBatchItemResult(
    int Index,
    string? Label,
    ResolvedSymbolSummary? Symbol,
    bool IsAmbiguous,
    IReadOnlyList<ResolveSymbolCandidate> Candidates,
    ErrorInfo? Error = null);

public sealed record ResolveSymbolsBatchResult(
    IReadOnlyList<ResolveSymbolsBatchItemResult> Results,
    int TotalCount,
    int ResolvedCount,
    int AmbiguousCount,
    int ErrorCount,
    ErrorInfo? Error = null);

public sealed record UnderstandCodebaseResult(
    string Profile,
    IReadOnlyList<ModuleSummary> Modules,
    IReadOnlyList<HotspotSummary> Hotspots,
    ErrorInfo? Error = null);

public sealed record ExplainSymbolRequest(string? SymbolId = null, string? Path = null, int? Line = null, int? Column = null);

public sealed record ImpactHint(string Zone, string Reason, int ReferenceCount);

public sealed record SymbolDocumentationParameter(
    string Name,
    string Description);

public sealed record SymbolDocumentationInfo(
    string? Summary = null,
    string? Returns = null,
    IReadOnlyList<SymbolDocumentationParameter>? Parameters = null);

public sealed record ExplainSymbolResult(
    CompactSymbolSummary? Symbol,
    string RoleSummary,
    string Signature,
    IReadOnlyList<ReferenceFileGroup>? KeyReferences,
    IReadOnlyList<ImpactHint> ImpactHints,
    SymbolDocumentationInfo? Documentation = null,
    ErrorInfo? Error = null);

public sealed record FlowTransition(
    string FromProject,
    string ToProject,
    int Count,
    IReadOnlyList<string>? UncertaintyCategories = null);

public static class FlowEvidenceKinds
{
    public const string DirectStatic = "direct_static";
    public const string PossibleTarget = "possible_target";
}

public static class FlowUncertaintyCategories
{
    public const string InterfaceDispatch = "interface_dispatch";
    public const string PolymorphicInference = "polymorphic_inference";
    public const string ReflectionBlindspot = "reflection_blindspot";
    public const string DynamicUnresolved = "dynamic_unresolved";
    public const string UnresolvedProject = "unresolved_project";
    public const string ProjectInferenceDegraded = "project_inference_degraded";
}

public sealed record TraceRootSummary(
    string Name,
    string Kind,
    string? Owner,
    SourceLocation? Location);

public sealed record TraceSymbolEntry(
    string Display,
    SourceLocation? Location);

public sealed record TraceFlowEdge(
    string From,
    string To,
    SourceLocation Site,
    string Kind,
    IReadOnlyList<string>? UncertaintyCategories = null,
    IReadOnlyList<string>? RelatedSymbolIds = null);

public sealed record FlowUncertainty(
    string Category,
    string Message,
    SourceLocation? Location = null,
    SymbolReference? RelatedSymbol = null);

public sealed record TraceFlowRequest(
    string? SymbolId = null,
    string? Path = null,
    int? Line = null,
    int? Column = null,
    string? Direction = null,
    int? Depth = null,
    bool IncludePossibleTargets = false);

public sealed record TraceFlowResult(
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

public sealed record FindCodeSmellsRequest(
    string Path,
    int? MaxFindings = null,
    IReadOnlyList<string>? RiskLevels = null,
    IReadOnlyList<string>? Categories = null,
    string? ReviewMode = null);

public static class CodeSmellReviewModes
{
    public const string Default = "default";
    public const string Conservative = "conservative";
}

public static class CodeSmellReviewKinds
{
    public const string StyleSuggestion = "style_suggestion";
    public const string CodeFixHint = "code_fix_hint";
    public const string ReviewConcern = "review_concern";
}

public sealed record CodeSmellsSummary(
    int TotalFindings,
    int TotalOccurrences);

public sealed record CodeSmellOccurrenceFile(
    string FilePath,
    IReadOnlyList<ReferencePosition> Locations);

public sealed record CodeSmellFindingEntry(
    string FindingKey,
    string Title,
    string RiskLevel,
    string Category,
    string ReviewKind,
    int OccurrenceCount,
    IReadOnlyList<CodeSmellOccurrenceFile> OccurrenceFiles);

public sealed record FindCodeSmellsResult(
    CodeSmellsSummary Summary,
    IReadOnlyList<CodeSmellFindingEntry> Findings,
    IReadOnlyList<string>? Warnings,
    ResultContextMetadata Context,
    ErrorInfo? Error = null);
