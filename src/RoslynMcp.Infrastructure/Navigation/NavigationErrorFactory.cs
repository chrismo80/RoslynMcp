using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;

namespace RoslynMcp.Infrastructure.Navigation;

internal static class NavigationErrorFactory
{
    public static ErrorInfo CreateError(string code, string message, params (string Key, string? Value)[] details)
    {
        if (details.Length == 0)
        {
            return new ErrorInfo(code, message);
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in details)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                map[key] = value;
            }
        }

        return map.Count == 0 ? new ErrorInfo(code, message) : new ErrorInfo(code, message, map);
    }

    public static ErrorInfo? TryCreateInvalidSymbolIdError(string symbolId, string operation)
    {
        if (!string.IsNullOrWhiteSpace(symbolId))
        {
            return null;
        }

        return CreateError(
            ErrorCodes.InvalidInput,
            "symbolId must be a non-empty, non-whitespace string.",
            ("parameter", "symbolId"),
            ("operation", operation));
    }
}
