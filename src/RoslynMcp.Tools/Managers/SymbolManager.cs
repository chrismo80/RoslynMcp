using System.Collections.Concurrent;

namespace RoslynMcp.Tools.Managers;

internal sealed class SymbolManager
{
    private int _counter;
    
    private readonly ConcurrentDictionary<string, string> _outerSymbolIds = new();
    private readonly ConcurrentDictionary<string, string> _innerSymbolIds = new();

    internal string ToInnerSymbolId(string outerSymbolId) => _innerSymbolIds[outerSymbolId];

    internal string ToOuterSymbolId(string innerSymbolId)
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
