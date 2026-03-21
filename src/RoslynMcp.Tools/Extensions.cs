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

	extension(string? path)
	{
		public string ToWorkspaceAbsolutePath(string workspaceRoot)
		{
			if (string.IsNullOrWhiteSpace(path))
				return path!;

			var trimmedPath = path.Trim();
			try
			{
				return Path.IsPathRooted(trimmedPath)
					? Path.GetFullPath(trimmedPath)
					: Path.GetFullPath(trimmedPath, workspaceRoot);
			}
			catch
			{
				return trimmedPath;
			}
		}

		public string ToWorkspaceRelativePathIfPossible(string workspaceRoot)
		{
			if (string.IsNullOrWhiteSpace(path))
				return path!;

			var absolutePath = path.ToWorkspaceAbsolutePath(workspaceRoot);
			if (!Path.IsPathRooted(absolutePath))
				return absolutePath;

			try
			{
				var normalizedWorkspaceRoot = workspaceRoot.EnsureTrailingDirectorySeparator();
				var normalizedAbsolutePath = Path.GetFullPath(absolutePath);
				if (!normalizedAbsolutePath.StartsWith(normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
					return normalizedAbsolutePath;

				return Path.GetRelativePath(workspaceRoot, normalizedAbsolutePath);
			}
			catch
			{
				return absolutePath;
			}
		}
	}

	private static string EnsureTrailingDirectorySeparator(this string path)
		=> path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
			? path
			: path + Path.DirectorySeparatorChar;
}