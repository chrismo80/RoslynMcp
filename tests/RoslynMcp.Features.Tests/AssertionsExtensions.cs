using Is.Assertions;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tests.ToolTests;

namespace RoslynMcp.Features.Tests;

public static class AssertionsExtensions
{
    extension(string text)
    {
        internal void ShouldNotBeEmpty()
        {
            string.IsNullOrEmpty(text).IsFalse();
        }
    }
    
    extension(ErrorInfo? error)
    {
        internal void ShouldBeNone()
        {
            error.IsNull();
        }

        internal void ShouldHaveCode(string expectedCode)
        {
            error.IsNotNull();
            error!.Code.Is(expectedCode);
        }
    }

    extension(ResolvedSymbolSummary? symbol)
    {
        internal void ShouldMatchResolvedSymbol(string expectedDisplayName, string expectedKind, string expectedFileName)
        {
            symbol.IsNotNull();
            symbol!.DisplayName.Is(expectedDisplayName);
            symbol.Kind.Is(expectedKind);
            symbol.FilePath.EndsWith(expectedFileName, StringComparison.OrdinalIgnoreCase).IsTrue();
            symbol.SymbolId.ShouldNotBeEmpty();
        }
    }
    
    extension(IReadOnlyList<SourceLocation> references)
    {
        internal void ShouldMatchReferences(params (string FileName, int Line)[] expected)
        {
            references.Count.Is(expected.Length);

            for (var i = 0; i < expected.Length; i++)
            {
                references[i].FilePath.EndsWith(expected[i].FileName, StringComparison.OrdinalIgnoreCase).IsTrue();
                references[i].Line.Is(expected[i].Line);
            }
        }
    }

    extension(IReadOnlyList<CodeSmellMatch> actual)
    {
        internal void ShouldMatchFindings(ExpectedCodeSmellFinding[] expected)
        {
            actual.Count.Is(expected.Length);

            for (var i = 0; i < expected.Length; i++)
            {
                var actualFinding = actual[i];
                var expectedFinding = expected[i];

                actualFinding.Location.Line.Is(expectedFinding.Line);
                actualFinding.Location.Column.Is(expectedFinding.Column);
                actualFinding.Title.Is(expectedFinding.Title);
                actualFinding.Category.Is(expectedFinding.Category);
                actualFinding.RiskLevel.Is(expectedFinding.RiskLevel);
            }
        }
    }
}
