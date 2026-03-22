using RoslynMcp.Tools.Mutation;

namespace RoslynMcp.Tools.Mutation.FormatDocument;

public sealed record Request(string Path);
public sealed record Result(string Path, bool WasFormatted, ErrorInfo? Error = null);
