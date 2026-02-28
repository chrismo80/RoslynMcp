using RoslynMcp.Core.Models.Common;

namespace RoslynMcp.Infrastructure.Agent;

internal static class AgentErrorInfo
{
    public static ErrorInfo Create(string code, string message, string nextAction, params (string Key, string? Value)[] details)
    {
        var map = BuildDetails(nextAction, details);
        return new ErrorInfo(code, message, map);
    }

    public static ErrorInfo? Normalize(ErrorInfo? error, string nextAction)
    {
        if (error == null)
        {
            return null;
        }

        if (error.Details != null && error.Details.TryGetValue("nextAction", out var existing) && !string.IsNullOrWhiteSpace(existing))
        {
            return error;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (error.Details != null)
        {
            foreach (var pair in error.Details)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    map[pair.Key] = pair.Value;
                }
            }
        }

        map["nextAction"] = nextAction;
        return new ErrorInfo(error.Code, error.Message, map);
    }

    private static IReadOnlyDictionary<string, string> BuildDetails(string nextAction, params (string Key, string? Value)[] details)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nextAction"] = nextAction
        };

        foreach (var (key, value) in details)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                map[key] = value;
            }
        }

        return map;
    }
}
