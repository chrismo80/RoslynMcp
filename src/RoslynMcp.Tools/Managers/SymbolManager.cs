using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Managers;

public sealed class SymbolManager : Manager
{
    private int _counter;
    
    private readonly ConcurrentDictionary<ISymbol, string> _outerSymbolIds = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<string, ISymbol> _innerSymbolIds = new();

    internal ISymbol ToInnerSymbolId(string outerSymbolId) => _innerSymbolIds[outerSymbolId];

    internal string ToOuterSymbolId(ISymbol innerSymbolId)
    {
        if(_outerSymbolIds.TryGetValue(innerSymbolId, out var outerSymbolId))
            return outerSymbolId;
        
        outerSymbolId =  NewOuterSymbol();
        
        _outerSymbolIds[innerSymbolId] = outerSymbolId;
        _innerSymbolIds[outerSymbolId] = innerSymbolId;
        
        return outerSymbolId;
    }

    internal void Clear()
    {
        _outerSymbolIds.Clear();
        _innerSymbolIds.Clear();
    }
    
    private string NewOuterSymbol()
    {
        Interlocked.Increment(ref _counter);
        
        return $"S-{_counter:00000}";
    }
}
