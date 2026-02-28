using RoslynMcp.Core.Models.Navigation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcp.Infrastructure.Navigation;

internal sealed class SymbolLookupService : ISymbolLookupService
{
    public async Task<ISymbol?> ResolveSymbolAsync(string symbolId, Solution solution, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbolId))
        {
            return null;
        }

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                continue;
            }

            var resolved = SymbolIdentity.Resolve(symbolId, compilation, ct);
            if (resolved != null)
            {
                return resolved.OriginalDefinition ?? resolved;
            }
        }

        return null;
    }

    public async Task<(ISymbol? Symbol, Project? OwnerProject)> ResolveSymbolWithProjectAsync(string symbolId, Solution solution, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbolId))
        {
            return (null, null);
        }

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                continue;
            }

            var resolved = SymbolIdentity.Resolve(symbolId, compilation, ct);
            if (resolved != null)
            {
                return (resolved.OriginalDefinition ?? resolved, project);
            }
        }

        return (null, null);
    }

    public async Task<(IReadOnlyList<SymbolDescriptor> Symbols, int TotalCount)> SearchSymbolsAsync(
        Solution solution,
        string query,
        int offset,
        int limit,
        CancellationToken ct)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<ISymbol>();

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var symbols = await SymbolFinder.FindDeclarationsAsync(
                    project,
                    query,
                    ignoreCase: true,
                    SymbolFilter.TypeAndMember,
                    ct)
                .ConfigureAwait(false);

            foreach (var symbol in symbols)
            {
                var id = SymbolIdentity.CreateId(symbol);
                if (!unique.Add(id))
                {
                    continue;
                }

                candidates.Add(symbol);
            }
        }

        var ordered = candidates
            .Select(NavigationModelUtilities.CreateDescriptor)
            .OrderBy(static descriptor => descriptor, SymbolDescriptorComparer.Instance)
            .ToList();

        var total = ordered.Count;
        var descriptors = ordered.Skip(offset).Take(limit).ToArray();
        return (descriptors, total);
    }

    public async Task<ISymbol?> GetSymbolAtPositionAsync(Solution solution, string path, int line, int column, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var document = solution.Projects
            .SelectMany(static p => p.Documents)
            .FirstOrDefault(d => NavigationModelUtilities.MatchesByNormalizedPath(d.FilePath, path));
        if (document == null)
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (root == null || model == null)
        {
            return null;
        }

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        if (line <= 0 || column <= 0 || line > text.Lines.Count)
        {
            return null;
        }

        var textLine = text.Lines[line - 1];
        var position = textLine.Start + Math.Min(column - 1, textLine.End - textLine.Start);
        var token = root.FindToken(position);
        if (token.RawKind == 0)
        {
            return null;
        }

        var node = token.Parent;
        while (node != null)
        {
            var symbol = model.GetDeclaredSymbol(node, ct) ?? model.GetSymbolInfo(node, ct).Symbol;
            if (symbol != null)
            {
                return symbol.OriginalDefinition ?? symbol;
            }

            node = node.Parent;
        }

        return null;
    }

    public async Task<(IReadOnlyList<SymbolDescriptor> Symbols, int TotalCount)> SearchSymbolsScopedAsync(
        Solution solution,
        string query,
        string scope,
        string? path,
        string? kind,
        string? accessibility,
        int offset,
        int limit,
        CancellationToken ct)
    {
        var selectedProjects = SelectProjects(solution, scope, path).ToArray();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<ISymbol>();

        foreach (var project in selectedProjects)
        {
            ct.ThrowIfCancellationRequested();
            var symbols = await SymbolFinder.FindDeclarationsAsync(
                    project,
                    query,
                    ignoreCase: true,
                    SymbolFilter.TypeAndMember,
                    ct)
                .ConfigureAwait(false);

            foreach (var symbol in symbols)
            {
                if (!MatchesPathScope(symbol, scope, path))
                {
                    continue;
                }

                if (!MatchesKind(symbol, kind) || !MatchesAccessibility(symbol, accessibility))
                {
                    continue;
                }

                var id = SymbolIdentity.CreateId(symbol);
                if (!unique.Add(id))
                {
                    continue;
                }

                candidates.Add(symbol);
            }
        }

        var ordered = candidates
            .Select(NavigationModelUtilities.CreateDescriptor)
            .OrderBy(static descriptor => descriptor, SymbolDescriptorComparer.Instance)
            .ToList();

        var total = ordered.Count;
        var descriptors = ordered.Skip(offset).Take(limit).ToArray();
        return (descriptors, total);
    }

    private static IEnumerable<Project> SelectProjects(Solution solution, string scope, string? path)
    {
        if (string.Equals(scope, SymbolSearchScopes.Solution, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(path))
        {
            return solution.Projects;
        }

        if (string.Equals(scope, SymbolSearchScopes.Project, StringComparison.Ordinal))
        {
            return solution.Projects.Where(project =>
                NavigationModelUtilities.MatchesByNormalizedPath(project.FilePath, path) ||
                string.Equals(project.Name, path, StringComparison.OrdinalIgnoreCase));
        }

        return solution.Projects.Where(project =>
            project.Documents.Any(d => NavigationModelUtilities.MatchesByNormalizedPath(d.FilePath, path)));
    }

    private static bool MatchesPathScope(ISymbol symbol, string scope, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(scope, SymbolSearchScopes.Solution, StringComparison.Ordinal))
        {
            return true;
        }

        var locations = symbol.Locations.Where(static location => location.IsInSource).ToArray();
        if (locations.Length == 0)
        {
            return false;
        }

        if (string.Equals(scope, SymbolSearchScopes.Document, StringComparison.Ordinal))
        {
            return locations.Any(location => NavigationModelUtilities.MatchesByNormalizedPath(location.SourceTree?.FilePath, path));
        }

        return locations.Any(location =>
        {
            var sourcePath = location.SourceTree?.FilePath;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return false;
            }

            try
            {
                var normalizedSource = System.IO.Path.GetFullPath(sourcePath);
                var normalizedScopePath = System.IO.Path.GetFullPath(path);
                return normalizedSource.StartsWith(System.IO.Path.GetDirectoryName(normalizedScopePath) ?? normalizedScopePath,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return NavigationModelUtilities.MatchesByNormalizedPath(sourcePath, path);
            }
        });
    }

    private static bool MatchesKind(ISymbol symbol, string? kind)
        => string.IsNullOrWhiteSpace(kind) || string.Equals(symbol.Kind.ToString(), kind, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAccessibility(ISymbol symbol, string? accessibility)
        => string.IsNullOrWhiteSpace(accessibility) ||
           string.Equals(symbol.DeclaredAccessibility.ToString(), accessibility, StringComparison.OrdinalIgnoreCase);
}
