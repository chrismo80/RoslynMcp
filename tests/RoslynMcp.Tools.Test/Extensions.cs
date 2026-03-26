using System.Text.Encodings.Web;
using System.Text.Json;

namespace RoslynMcp.Tools.Test;

public static class Extensions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    
    extension(object result)
    {
        internal string ToJson() => JsonSerializer.Serialize(result, Options);
    }
}