using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tools.Inspection.ListMembers;

internal static class Extensions
{
    extension(ISymbol symbol)
    {
        public string? ToMemberKind()
        {
            return symbol switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "ctor",
                IMethodSymbol method when method.MethodKind == MethodKind.Ordinary || method.MethodKind == MethodKind.UserDefinedOperator
                                                                                   || method.MethodKind == MethodKind.Conversion || method.MethodKind == MethodKind.ReducedExtension
                                                                                   || method.MethodKind == MethodKind.DelegateInvoke => "method",
                IPropertySymbol => "property",
                IFieldSymbol field when !field.IsImplicitlyDeclared => "field",
                IEventSymbol => "event",
                _ => null
            };
        }
    }
}
