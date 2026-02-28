using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Reflection;

namespace RoslynMcp.Infrastructure.Navigation;

internal static class SymbolIdentity
{
    private static readonly MethodInfo s_createString;
    private static readonly MethodInfo s_resolveString;
    private static readonly PropertyInfo s_resolutionSymbol;

    static SymbolIdentity()
    {
        var assembly = typeof(SymbolFinder).Assembly;
        var symbolKeyType = assembly.GetType("Microsoft.CodeAnalysis.SymbolKey", throwOnError: true)!;
        var resolutionType = assembly.GetType("Microsoft.CodeAnalysis.SymbolKeyResolution", throwOnError: true)!;

        s_createString = symbolKeyType.GetMethod("CreateString", BindingFlags.Public | BindingFlags.Static,
                             binder: null,
                             types: new[] { typeof(ISymbol), typeof(CancellationToken) },
                             modifiers: null)
            ?? throw new InvalidOperationException("Unable to locate SymbolKey.CreateString");

        s_resolveString = symbolKeyType.GetMethod("ResolveString", BindingFlags.Public | BindingFlags.Static,
                              binder: null,
                              types: new[] { typeof(string), typeof(Compilation), typeof(bool), typeof(CancellationToken) },
                              modifiers: null)
            ?? throw new InvalidOperationException("Unable to locate SymbolKey.ResolveString");

        s_resolutionSymbol = resolutionType.GetProperty("Symbol", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Unable to locate SymbolKeyResolution.Symbol");
    }

    public static string CreateId(ISymbol symbol)
    {
        var resolved = symbol.OriginalDefinition ?? symbol;
        var result = (string?)s_createString.Invoke(null, new object?[] { resolved, CancellationToken.None });
        if (!string.IsNullOrEmpty(result))
        {
            return result;
        }

        return resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public static ISymbol? Resolve(string identifier, Compilation compilation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var resolution = s_resolveString.Invoke(null, new object?[] { identifier, compilation, true, ct });
        if (resolution == null)
        {
            return null;
        }

        return (ISymbol?)s_resolutionSymbol.GetValue(resolution);
    }
}
