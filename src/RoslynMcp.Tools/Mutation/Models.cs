using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Tools.Mutation;

public sealed record ErrorInfo(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);

public sealed record MutationDiagnosticInfo(
    string Id,
    string Severity,
    string Message,
    string FilePath,
    int Line,
    int Column,
    string? Origin = null);

public sealed record DiagnosticsDeltaInfo(
    IReadOnlyList<MutationDiagnosticInfo> NewErrors,
    IReadOnlyList<MutationDiagnosticInfo> NewWarnings);

public sealed record MethodParameterSpec(string Name, string Type);

public sealed record MethodInsertionSpec(
    string Name,
    string ReturnType,
    string Accessibility,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<MethodParameterSpec> Parameters,
    string Body);

internal sealed record MethodTypeTarget(
    INamedTypeSymbol TypeSymbol,
    TypeDeclarationSyntax Declaration,
    Document Document);

internal sealed record MethodDeclarationTarget(
    IMethodSymbol MethodSymbol,
    MethodDeclarationSyntax Declaration,
    Document Document);
