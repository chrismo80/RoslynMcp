using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;

namespace RoslynMcp.Infrastructure.Refactoring;

internal readonly record struct ProviderCodeFixKey(string ProviderTypeName, string DiagnosticId, string? EquivalenceKey, string ActionTitle);

internal readonly record struct ProviderRefactoringKey(string ProviderTypeName, string? EquivalenceKey, string ActionTitle);

internal sealed record ProviderCodeActionCandidate(string ProviderTypeName, CodeAction Action);

internal sealed record DiscoveredAction(
    string Title,
    string Category,
    string Origin,
    string ProviderActionKey,
    string FilePath,
    int SpanStart,
    int SpanLength,
    SourceLocation Location,
    string? DiagnosticId,
    string? RefactoringId);

internal sealed record ActionExecutionIdentity(
    int WorkspaceVersion,
    string PolicyProfile,
    string Origin,
    string Category,
    string ProviderActionKey,
    string FilePath,
    int SpanStart,
    int SpanLength,
    string? DiagnosticId,
    string? RefactoringId,
    SourceLocation Location)
{
    public DiscoveredAction ToDiscoveredAction()
        => new(
            string.Empty,
            Category,
            Origin,
            ProviderActionKey,
            FilePath,
            SpanStart,
            SpanLength,
            Location,
            DiagnosticId,
            RefactoringId);
}

internal sealed record PolicyAssessment(string Decision, string RiskLevel, string ReasonCode, string ReasonMessage);

internal sealed record ParsedFixId(int WorkspaceVersion, string DiagnosticId, int SpanStart, int SpanLength, string FilePath);

internal sealed record FixOperation(string Title, Func<Solution, CancellationToken, Task<Solution>> ApplyAsync);
