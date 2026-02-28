using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Analysis;

internal interface IRoslynSymbolIdFactory
{
    string CreateId(ISymbol symbol);
}
