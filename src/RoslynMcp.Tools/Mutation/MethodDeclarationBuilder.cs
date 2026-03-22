using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace RoslynMcp.Tools.Mutation;

internal sealed class MethodDeclarationBuilder
{
    private static readonly Dictionary<string, SyntaxKind> AccessibilityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["public"] = SyntaxKind.PublicKeyword,
        ["internal"] = SyntaxKind.InternalKeyword,
        ["private"] = SyntaxKind.PrivateKeyword,
        ["protected"] = SyntaxKind.ProtectedKeyword,
        ["protected_internal"] = SyntaxKind.ProtectedKeyword,
        ["private_protected"] = SyntaxKind.PrivateKeyword
    };

    private static readonly Dictionary<string, SyntaxKind[]> AccessibilityCompositeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["protected_internal"] = [SyntaxKind.ProtectedKeyword, SyntaxKind.InternalKeyword],
        ["private_protected"] = [SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword]
    };

    private static readonly Dictionary<string, SyntaxKind> ModifierMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["static"] = SyntaxKind.StaticKeyword,
        ["async"] = SyntaxKind.AsyncKeyword,
        ["virtual"] = SyntaxKind.VirtualKeyword,
        ["override"] = SyntaxKind.OverrideKeyword,
        ["sealed"] = SyntaxKind.SealedKeyword,
        ["new"] = SyntaxKind.NewKeyword
    };

    public bool TryBuild(MethodInsertionSpec spec, out MethodDeclarationSyntax? method, out ErrorInfo? error)
    {
        method = null;
        error = Validate(spec);
        if (error is not null)
            return false;

        var returnType = SyntaxFactory.ParseTypeName(spec.ReturnType);
        if (HasSyntaxErrors(returnType))
        {
            error = CreateInvalidSpecError($"returnType '{spec.ReturnType}' could not be parsed.", ("field", "returnType"));
            return false;
        }

        var parameterNodes = new List<ParameterSyntax>(spec.Parameters.Count);
        for (var index = 0; index < spec.Parameters.Count; index++)
        {
            var parameter = spec.Parameters[index];
            var parameterType = SyntaxFactory.ParseTypeName(parameter.Type);
            if (HasSyntaxErrors(parameterType))
            {
                error = CreateInvalidSpecError($"parameter type '{parameter.Type}' for parameter '{parameter.Name}' could not be parsed.", ("field", $"parameters[{index}]"));
                return false;
            }

            parameterNodes.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameter.Name)).WithType(parameterType));
        }

        if (!TryParseBody(spec.Body, out var body, out error))
            return false;

        method = SyntaxFactory.MethodDeclaration(returnType, SyntaxFactory.Identifier(spec.Name))
            .WithModifiers(BuildModifiers(spec.Accessibility, spec.Modifiers))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameterNodes)))
            .WithBody(body)
            .WithAdditionalAnnotations(Formatter.Annotation);
        return true;
    }

    public static bool TryParseBody(string body, out BlockSyntax? block, out ErrorInfo? error)
    {
        block = SyntaxFactory.ParseStatement("{" + Environment.NewLine + body + Environment.NewLine + "}") as BlockSyntax;
        if (block is not null && !HasSyntaxErrors(block))
        {
            error = null;
            return true;
        }

        error = CreateInvalidSpecError("method body could not be parsed as a valid block-bodied method.", ("field", "body"));
        return false;
    }

    private static ErrorInfo? Validate(MethodInsertionSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Name) || !SyntaxFacts.IsValidIdentifier(spec.Name))
            return CreateInvalidSpecError("name must be a valid C# identifier.", ("field", "name"));

        if (string.IsNullOrWhiteSpace(spec.ReturnType))
            return CreateInvalidSpecError("returnType must be provided.", ("field", "returnType"));

        if (string.IsNullOrWhiteSpace(spec.Accessibility) || !AccessibilityMap.ContainsKey(spec.Accessibility))
            return CreateInvalidSpecError("accessibility must be one of: public, internal, private, protected, protected_internal, private_protected.", ("field", "accessibility"));

        if (spec.Modifiers is null || spec.Parameters is null || spec.Body is null)
            return CreateInvalidSpecError("modifiers, parameters, and body must be provided.");

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < spec.Parameters.Count; index++)
        {
            var parameter = spec.Parameters[index];
            if (string.IsNullOrWhiteSpace(parameter.Name) || !SyntaxFacts.IsValidIdentifier(parameter.Name))
                return CreateInvalidSpecError($"parameter name at index {index} must be a valid C# identifier.", ("field", $"parameters[{index}]"));

            if (!seenNames.Add(parameter.Name))
                return CreateInvalidSpecError($"parameter name '{parameter.Name}' is duplicated.", ("field", $"parameters[{index}]"));

            if (string.IsNullOrWhiteSpace(parameter.Type))
                return CreateInvalidSpecError($"parameter type for '{parameter.Name}' must be provided.", ("field", $"parameters[{index}]"));
        }

        foreach (var modifier in spec.Modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifier) || !ModifierMap.ContainsKey(modifier))
                return CreateInvalidSpecError($"unsupported modifier '{modifier}'. Supported modifiers: static, async, virtual, override, sealed, new.", ("field", "modifiers"));
        }

        return null;
    }

    private static SyntaxTokenList BuildModifiers(string accessibility, IReadOnlyList<string> modifiers)
    {
        var tokens = new List<SyntaxToken>();
        if (AccessibilityCompositeMap.TryGetValue(accessibility, out var compositeKinds))
            tokens.AddRange(compositeKinds.Select(SyntaxFactory.Token));
        else
            tokens.Add(SyntaxFactory.Token(AccessibilityMap[accessibility]));

        foreach (var modifier in modifiers)
            tokens.Add(SyntaxFactory.Token(ModifierMap[modifier]));

        return SyntaxFactory.TokenList(tokens);
    }

    private static bool HasSyntaxErrors(CSharpSyntaxNode node)
        => node.ContainsDiagnostics || node.GetDiagnostics().Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) || node.DescendantTokens(descendIntoTrivia: true).Any(static token => token.IsMissing);

    private static ErrorInfo CreateInvalidSpecError(string message, params (string Key, string? Value)[] details)
        => CreateError("invalid_method_specification", message, details);

    internal static ErrorInfo CreateError(string code, string message, params (string Key, string? Value)[] details)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in details)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                map[key] = value;
        }

        return new ErrorInfo(code, message, map.Count == 0 ? null : map);
    }
}
