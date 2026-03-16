using Is.Assertions;
using RoslynMcp.Core.Models;
using System.Text.RegularExpressions;

namespace RoslynMcp.Features.Tests;

public static partial class AssertionsExtensions
{
    [GeneratedRegex(@"^S\-\d{4,}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExternalSymbolIdPattern();
    
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

    internal static void ShouldBeExternalSymbolId(this string symbolId)
    {
        symbolId.ShouldNotBeEmpty();
        ExternalSymbolIdPattern().IsMatch(symbolId).IsTrue();
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
        symbol.Location.IsNotNull();
        symbol.Location!.FilePath.ShouldEndWithPathSuffix(expectedFileName);
        symbol.SymbolId.ShouldBeExternalSymbolId();
    }

    internal static void ShouldMatchReferences(this IReadOnlyList<ReferenceFileGroup> referenceFiles, params (string FileName, int Line)[] expected)
    {
        var references = referenceFiles
            .SelectMany(static file => file.References.Select(position => new SourceLocation(file.FilePath, position.Line, position.Column)))
            .ToArray();

        references.Length.Is(expected.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            references[i].FilePath.ShouldEndWithPathSuffix(expected[i].FileName);
            references[i].Line.Is(expected[i].Line);
        }
    }

    internal static string FilePathOrEmpty(this SourceLocation? location)
        => location?.FilePath ?? string.Empty;

    internal static int? LineOrNull(this SourceLocation? location)
        => location?.Line;

    private static string NormalizePathSeparators(string path)
    {
        return path.Replace('/', '\\');
    }
}
