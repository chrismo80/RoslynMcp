using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Reflection;

namespace RoslynMcp.Infrastructure.Analysis;

internal sealed class RoslynSymbolIdFactory : IRoslynSymbolIdFactory
{
    private static readonly MethodInfo s_createString;

    static RoslynSymbolIdFactory()
    {
        var assembly = typeof(SymbolFinder).Assembly;
        var symbolKeyType = assembly.GetType("Microsoft.CodeAnalysis.SymbolKey", throwOnError: true)!;
        s_createString = symbolKeyType.GetMethod("CreateString", BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(ISymbol), typeof(CancellationToken) },
            modifiers: null)
            ?? throw new InvalidOperationException("Unable to locate SymbolKey.CreateString");
    }

    public string CreateId(ISymbol symbol)
    {
        var resolved = symbol.OriginalDefinition ?? symbol;
        var result = (string?)s_createString.Invoke(null, new object?[] { resolved, CancellationToken.None });
        if (!string.IsNullOrEmpty(result))
        {
            return result;
        }

        return resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
