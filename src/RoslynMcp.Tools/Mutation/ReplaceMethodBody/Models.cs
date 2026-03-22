using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.ReplaceMethodBody;

public sealed record Request(string TargetMethodSymbolId, string Body);
public sealed record ReplacedMethodBodyInfo(string MethodSymbolId, string Signature);
public sealed record Result(string Status, IReadOnlyList<string> ChangedFiles, string TargetMethodSymbolId, ReplacedMethodBodyInfo? ReplacedMethodBody, DiagnosticsDeltaInfo DiagnosticsDelta, ErrorInfo? Error = null);
