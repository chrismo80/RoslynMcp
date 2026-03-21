namespace RoslynMcp.Tools;

internal static class Extensions
{
    extension(string? input)
	{
		internal string? NormalizeOptional() =>
			string.IsNullOrWhiteSpace(input) ? null : input.Trim();
	}

	extension(string input)
	{
		internal string NormalizeEscapedTypeSyntax() => input
			.Replace("&lt;", "<", StringComparison.Ordinal)
			.Replace("&gt;", ">", StringComparison.Ordinal);

		internal string NormalizeEscapedNewlines() => input
			.Replace("\\r\\n", "\r\n", StringComparison.Ordinal)
			.Replace("\\n", "\n", StringComparison.Ordinal)
			.Replace("\\r", "\r", StringComparison.Ordinal);
	}
}