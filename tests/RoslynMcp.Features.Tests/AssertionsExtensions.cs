using Is.Assertions;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tests.Inspections.Tools;

namespace RoslynMcp.Features.Tests;

public static class AssertionsExtensions
{
    extension(string actualPath)
    {
        internal bool HasPathSuffix(string expectedPathSuffix)
        {
            return NormalizePathSeparators(actualPath).EndsWith(NormalizePathSeparators(expectedPathSuffix), StringComparison.OrdinalIgnoreCase);
        }

        internal void ShouldEndWithPathSuffix(string expectedPathSuffix)
        {
            actualPath.HasPathSuffix(expectedPathSuffix).IsTrue();
        }
    }

    internal static void ShouldNotBeEmpty(this string text)
    {
        string.IsNullOrEmpty(text).IsFalse();
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

    internal static void ShouldMatchResolvedSymbol(this ResolvedSymbolSummary? symbol, string expectedDisplayName, string expectedKind, string expectedFileName)
    {
        symbol.IsNotNull();
        symbol!.DisplayName.Is(expectedDisplayName);
        symbol.Kind.Is(expectedKind);
        symbol.FilePath.ShouldEndWithPathSuffix(expectedFileName);
        symbol.SymbolId.ShouldNotBeEmpty();
    }

    internal static void ShouldMatchReferences(this IReadOnlyList<SourceLocation> references, params (string FileName, int Line)[] expected)
    {
        references.Count.Is(expected.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            references[i].FilePath.ShouldEndWithPathSuffix(expected[i].FileName);
            references[i].Line.Is(expected[i].Line);
        }
    }

    internal static void ShouldMatchFindings(this IReadOnlyList<CodeSmellMatch> actual, ExpectedCodeSmellFinding[] expected)
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

    private static string NormalizePathSeparators(string path)
    {
        return path.Replace('/', '\\');
    }
}