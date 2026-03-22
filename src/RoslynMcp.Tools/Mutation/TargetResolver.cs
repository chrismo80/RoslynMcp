using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Tools.Infrastructure.Services;

namespace RoslynMcp.Tools.Mutation;

internal sealed class TargetResolver
{
    public async Task<(MethodTypeTarget? Target, ErrorInfo? Error)> ResolveTypeAsync(string targetTypeSymbolId, Solution solution, string operation, CancellationToken cancellationToken)
    {
        var symbol = await SymbolLookup.ResolveSymbolAsync(targetTypeSymbolId, solution, cancellationToken).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol typeSymbol)
            return (null, CreateError("symbol_not_found", $"Target type symbol '{targetTypeSymbolId}' could not be resolved.", ("targetTypeSymbolId", targetTypeSymbolId), ("operation", operation)));

        if (typeSymbol.DeclaringSyntaxReferences.Length != 1)
            return (null, CreateError("target_not_source_editable", "The target type must have exactly one source declaration to support deterministic insertion.", ("targetTypeSymbolId", targetTypeSymbolId), ("operation", operation)));

        var declarationSyntax = await typeSymbol.DeclaringSyntaxReferences[0].GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        if (declarationSyntax is not TypeDeclarationSyntax declaration)
            return (null, CreateError("target_not_source_editable", "The target symbol is not a source-editable type declaration.", ("targetTypeSymbolId", targetTypeSymbolId), ("operation", operation)));

        var document = solution.GetDocument(declaration.SyntaxTree);
        if (document is null)
            return (null, CreateError("target_not_source_editable", "The target type could not be mapped to an editable source document.", ("targetTypeSymbolId", targetTypeSymbolId), ("operation", operation)));

        return (new MethodTypeTarget(typeSymbol, declaration, document), null);
    }

    public async Task<(MethodDeclarationTarget? Target, ErrorInfo? Error)> ResolveMethodAsync(string targetMethodSymbolId, Solution solution, string operation, CancellationToken cancellationToken)
    {
        var symbol = await SymbolLookup.ResolveSymbolAsync(targetMethodSymbolId, solution, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
            return (null, CreateError("symbol_not_found", $"Target method symbol '{targetMethodSymbolId}' could not be resolved.", ("targetMethodSymbolId", targetMethodSymbolId), ("operation", operation)));

        if (symbol is not IMethodSymbol methodSymbol)
            return (null, CreateError("unsupported_symbol_kind", "Only ordinary source methods are supported.", ("targetMethodSymbolId", targetMethodSymbolId), ("operation", operation)));

        if (methodSymbol.MethodKind != MethodKind.Ordinary)
            return (null, CreateError("unsupported_symbol_kind", "Only ordinary source methods are supported.", ("targetMethodSymbolId", targetMethodSymbolId), ("operation", operation)));

        if (methodSymbol.DeclaringSyntaxReferences.Length != 1)
            return (null, CreateError("target_not_source_editable", "The target method must have exactly one source declaration.", ("targetMethodSymbolId", targetMethodSymbolId), ("operation", operation)));

        var declarationSyntax = await methodSymbol.DeclaringSyntaxReferences[0].GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        if (declarationSyntax is not MethodDeclarationSyntax declaration)
            return (null, CreateError("target_not_source_editable", "The target symbol is not a source-editable method declaration.", ("targetMethodSymbolId", targetMethodSymbolId), ("operation", operation)));

        var document = solution.GetDocument(declaration.SyntaxTree);
        if (document is null)
            return (null, CreateError("target_not_source_editable", "The target method could not be mapped to an editable source document.", ("targetMethodSymbolId", targetMethodSymbolId), ("operation", operation)));

        return (new MethodDeclarationTarget(methodSymbol, declaration, document), null);
    }

    private static ErrorInfo CreateError(string code, string message, params (string Key, string? Value)[] details)
        => MethodDeclarationBuilder.CreateError(code, message, details);
}
