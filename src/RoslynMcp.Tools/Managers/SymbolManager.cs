using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Managers;

public sealed class SymbolManager : Manager
{
    private int _counter;

    private readonly ConcurrentDictionary<ISymbol, string> _ids = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<string, ISymbol> _symbols = new();

    internal ISymbol? ToSymbol(string id) => _symbols.GetValueOrDefault(id);

    internal string ToId(ISymbol symbol)
    {
        if(_ids.TryGetValue(symbol, out var id))
            return id;

        id = symbol is ITypeSymbol ? NewId('T') : NewId('M');
        
        _ids[symbol] = id;
        _symbols[id] = symbol;

        return id;
    }

    internal void Clear()
    {
        _ids.Clear();
        _symbols.Clear();
    }

    private string NewId(char prefix)
    {
        Interlocked.Increment(ref _counter);

        return $"{prefix}-{_counter:00000}";
    }
}