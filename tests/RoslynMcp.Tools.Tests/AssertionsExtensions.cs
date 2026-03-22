using Is.Assertions;

namespace RoslynMcp.Tools.Tests;

internal static class AssertionsExtensions
{
    internal static bool HasPathSuffix(this string actualPath, string expectedPathSuffix)
        => actualPath.Replace('/', '\\').EndsWith(expectedPathSuffix.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    internal static void ShouldEndWithPathSuffix(this string actualPath, string expectedPathSuffix)
        => actualPath.HasPathSuffix(expectedPathSuffix).IsTrue();

    internal static void ShouldNotBeEmpty(this string text)
        => string.IsNullOrWhiteSpace(text).IsFalse();
}
