using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.DeleteMethod;

public sealed record Request(string TargetMethodSymbolId);
public sealed record DeletedMethodInfo(string SymbolId, string Signature);
public sealed record Result(string Status, IReadOnlyList<string> ChangedFiles, string TargetMethodSymbolId, DeletedMethodInfo? DeletedMethod, DiagnosticsDeltaInfo DiagnosticsDelta, ErrorInfo? Error = null);
