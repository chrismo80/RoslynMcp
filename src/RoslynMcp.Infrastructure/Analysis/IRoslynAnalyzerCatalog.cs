using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace RoslynMcp.Infrastructure.Analysis;

internal interface IRoslynAnalyzerCatalog
{
    (ImmutableArray<DiagnosticAnalyzer> Analyzers, Exception? Error) GetCatalog();
}
