using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Managers;

public sealed class SymbolManager : Manager
{
    private int _counter;

    private readonly ConcurrentDictionary<ISymbol, string> _ids = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<string, ISymbol> _symbols = new();

    internal ISymbol ToSymbol(string outerSymbolId) => _symbols[outerSymbolId];

    internal string ToId(ISymbol innerSymbolId)
    {
        if(_ids.TryGetValue(innerSymbolId, out var outerSymbolId))
            return outerSymbolId;

        outerSymbolId =  NewId();

        _ids[innerSymbolId] = outerSymbolId;
        _symbols[outerSymbolId] = innerSymbolId;

        return outerSymbolId;
    }

    internal void Clear()
    {
        _ids.Clear();
        _symbols.Clear();
    }

    private string NewId()
    {
        Interlocked.Increment(ref _counter);

        return $"S-{_counter:00000}";
    }
}