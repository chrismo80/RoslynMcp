using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.ReplaceMethod;

public sealed record Request(string TargetMethodSymbolId, string Name, string ReturnType, string Accessibility, IReadOnlyList<string>? Modifiers, IReadOnlyList<string>? Parameters, string Body);
public sealed record ReplacedMethodInfo(string OriginalSymbolId, string OriginalSignature, string NewSymbolId, string NewSignature);
public sealed record Result(string Status, IReadOnlyList<string> ChangedFiles, string TargetMethodSymbolId, ReplacedMethodInfo? ReplacedMethod, DiagnosticsDeltaInfo DiagnosticsDelta, ErrorInfo? Error = null);
