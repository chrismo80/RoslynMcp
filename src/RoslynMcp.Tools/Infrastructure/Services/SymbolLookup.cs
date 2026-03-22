using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Infrastructure.Services;

public sealed class SymbolLookup
{
    internal static async Task<ISymbol?> ResolveSymbolAsync(string symbolId, Solution solution, CancellationToken cancellationToken)
    {
        var normalizedSymbolId = symbolId.NormalizeOptional();

        if (normalizedSymbolId is null)
            return null;

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

            if (compilation is null)
                continue;

            foreach (var symbol in EnumerateSymbols(compilation.Assembly.GlobalNamespace))
            {
                if (string.Equals(symbol.ToStableId(), normalizedSymbolId, StringComparison.Ordinal)
                    || string.Equals((symbol.OriginalDefinition ?? symbol).ToStableId(), normalizedSymbolId, StringComparison.Ordinal))
                    return symbol;
            }
        }

        return null;
    }

    internal static async Task<(ISymbol? Symbol, Project? OwnerProject)> ResolveSymbolWithProjectAsync(string symbolId, Solution solution, CancellationToken cancellationToken)
    {
        var normalizedSymbolId = symbolId.NormalizeOptional();
        if (normalizedSymbolId is null)
            return (null, null);

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
                continue;

            foreach (var symbol in EnumerateSymbols(compilation.Assembly.GlobalNamespace))
            {
                if (string.Equals(symbol.ToStableId(), normalizedSymbolId, StringComparison.Ordinal)
                    || string.Equals((symbol.OriginalDefinition ?? symbol).ToStableId(), normalizedSymbolId, StringComparison.Ordinal))
                    return (symbol, project);
            }
        }

        return (null, null);
    }

    internal static async Task<ISymbol?> GetSymbolAtPositionAsync(Solution solution, string path, int line, int column, string workspaceRoot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var absolutePath = path.ToWorkspaceAbsolutePath(workspaceRoot);
        var normalizedRelativePath = Path.IsPathRooted(path)
            ? null
            : path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(document =>
                document.FilePath.MatchesByNormalizedPath(absolutePath)
                || (normalizedRelativePath is not null
                    && document.FilePath is not null
                    && document.FilePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                        .EndsWith(normalizedRelativePath, StringComparison.OrdinalIgnoreCase)));

        if (document is null)
            return null;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || model is null)
            return null;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

        if (line <= 0 || column <= 0 || line > text.Lines.Count)
            return null;

        var textLine = text.Lines[line - 1];
        var position = textLine.Start + Math.Min(column - 1, textLine.End - textLine.Start);
        var token = root.FindToken(position);

        if (token.RawKind == 0)
            return null;

        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            var symbol = model.GetDeclaredSymbol(node, cancellationToken) ?? model.GetSymbolInfo(node, cancellationToken).Symbol;
            if (symbol is not null)
                return symbol;
        }

        return null;
    }

    internal static Task<ISymbol?> GetSymbolAtPositionAsync(Solution solution, string path, int line, int column, CancellationToken cancellationToken)
        => GetSymbolAtPositionAsync(solution, path, line, column, RoslynMcp.Tools.Extensions.WorkspaceRoot, cancellationToken);

    private static IEnumerable<ISymbol> EnumerateSymbols(INamespaceSymbol root)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();

        foreach (var member in root.GetMembers().OrderBy(static member => member.Name, StringComparer.Ordinal))
            stack.Push(member);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            switch (current)
            {
                case INamedTypeSymbol namedType:
                    yield return namedType;

                    foreach (var member in namedType.GetMembers())
                    {
                        if (member is not IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise })
                            yield return member;
                    }

                    foreach (var nested in namedType.GetTypeMembers().OrderByDescending(static member => member.Name, StringComparer.Ordinal))
                        stack.Push(nested);
                    break;
                case INamespaceSymbol ns:
                    foreach (var member in ns.GetMembers().OrderByDescending(static member => member.Name, StringComparer.Ordinal))
                        stack.Push(member);
                    break;
            }
        }
    }
}
