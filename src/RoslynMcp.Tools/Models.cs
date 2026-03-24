namespace RoslynMcp.Tools;

public sealed record Location(string FilePath, int Line, int Column);

public sealed record ErrorInfo(
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);
