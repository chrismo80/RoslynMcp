using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.AddMethod;

public sealed record Request(string TargetTypeSymbolId, string Name, string ReturnType, string Accessibility, IReadOnlyList<string>? Modifiers, IReadOnlyList<string>? Parameters, string Body);
public sealed record AddedMethodInfo(string SymbolId, string Signature);
public sealed record Result(string Status, IReadOnlyList<string> ChangedFiles, string TargetTypeSymbolId, AddedMethodInfo? AddedMethod, DiagnosticsDeltaInfo DiagnosticsDelta, ErrorInfo? Error = null);
