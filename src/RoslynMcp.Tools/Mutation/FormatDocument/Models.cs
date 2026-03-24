namespace RoslynMcp.Tools.Mutation.FormatDocument;

public sealed record Request(string Path);

public sealed record Result(string Path, ErrorInfo? Error = null);

public sealed record ErrorInfo(
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);