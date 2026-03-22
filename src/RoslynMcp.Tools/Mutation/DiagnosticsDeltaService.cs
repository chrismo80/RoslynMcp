using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Mutation;

internal sealed class DiagnosticsDeltaService
{
    public static async Task<DiagnosticsDeltaInfo> GetDeltaAsync(Solution beforeSolution, Solution afterSolution, DocumentId documentId, CancellationToken cancellationToken)
    {
        var before = await CollectAsync(beforeSolution, documentId, cancellationToken).ConfigureAwait(false);
        var after = await CollectAsync(afterSolution, documentId, cancellationToken).ConfigureAwait(false);
        var beforeKeys = before.Select(CreateKey).ToHashSet(StringComparer.Ordinal);
        var introduced = after.Where(diagnostic => !beforeKeys.Contains(CreateKey(diagnostic))).ToArray();
        return new DiagnosticsDeltaInfo(
            [.. introduced.Where(static diagnostic => string.Equals(diagnostic.Severity, "error", StringComparison.Ordinal))],
            [.. introduced.Where(static diagnostic => string.Equals(diagnostic.Severity, "warning", StringComparison.Ordinal))]);
    }

    private static async Task<IReadOnlyList<MutationDiagnosticInfo>> CollectAsync(Solution solution, DocumentId documentId, CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(documentId);
        if (document is null)
            return [];

        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
        var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxTree is null || compilation is null)
            return [];

        var filePath = document.FilePath ?? document.Name;
        return [.. compilation.GetDiagnostics(cancellationToken)
            .Where(static diagnostic => diagnostic.Location.IsInSource)
            .Where(diagnostic => string.Equals(diagnostic.Location.SourceTree?.FilePath, filePath, StringComparison.OrdinalIgnoreCase) && diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(static diagnostic => ToDiagnosticInfo(diagnostic))
            .OrderBy(static diagnostic => diagnostic.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static diagnostic => diagnostic.Line)
            .ThenBy(static diagnostic => diagnostic.Column)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)];
    }

    private static MutationDiagnosticInfo ToDiagnosticInfo(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var start = span.StartLinePosition;
        return new MutationDiagnosticInfo(
            diagnostic.Id,
            diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning",
            diagnostic.GetMessage(),
            span.Path,
            start.Line + 1,
            start.Character + 1,
            "compiler");
    }

    private static string CreateKey(MutationDiagnosticInfo diagnostic)
        => string.Join("|", diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.FilePath, diagnostic.Line, diagnostic.Column, diagnostic.Origin ?? string.Empty);
}
