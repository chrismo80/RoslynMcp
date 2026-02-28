using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;

namespace RoslynMcp.Infrastructure.Navigation;

internal sealed class SymbolMemberOutlineComparer : IComparer<SymbolMemberOutline>
{
    public static readonly SymbolMemberOutlineComparer Instance = new();

    public int Compare(SymbolMemberOutline? x, SymbolMemberOutline? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var byName = StringComparer.Ordinal.Compare(x.Name, y.Name);
        if (byName != 0)
        {
            return byName;
        }

        var byKind = StringComparer.Ordinal.Compare(x.Kind, y.Kind);
        if (byKind != 0)
        {
            return byKind;
        }

        var bySignature = StringComparer.Ordinal.Compare(x.Signature, y.Signature);
        if (bySignature != 0)
        {
            return bySignature;
        }

        var byAccessibility = StringComparer.Ordinal.Compare(x.Accessibility, y.Accessibility);
        if (byAccessibility != 0)
        {
            return byAccessibility;
        }

        return x.IsStatic.CompareTo(y.IsStatic);
    }
}

internal sealed class SourceLocationComparer : IComparer<SourceLocation>
{
    public static readonly SourceLocationComparer Instance = new();

    public int Compare(SourceLocation? x, SourceLocation? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var byPath = StringComparer.Ordinal.Compare(x.FilePath, y.FilePath);
        if (byPath != 0)
        {
            return byPath;
        }

        var byLine = x.Line.CompareTo(y.Line);
        if (byLine != 0)
        {
            return byLine;
        }

        return x.Column.CompareTo(y.Column);
    }
}

internal sealed class SymbolDescriptorComparer : IComparer<SymbolDescriptor>
{
    public static readonly SymbolDescriptorComparer Instance = new();

    public int Compare(SymbolDescriptor? x, SymbolDescriptor? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var byNameIgnoreCase = StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        if (byNameIgnoreCase != 0)
        {
            return byNameIgnoreCase;
        }

        var byName = StringComparer.Ordinal.Compare(x.Name, y.Name);
        if (byName != 0)
        {
            return byName;
        }

        var byKind = StringComparer.Ordinal.Compare(x.Kind, y.Kind);
        if (byKind != 0)
        {
            return byKind;
        }

        var byNamespace = StringComparer.Ordinal.Compare(x.ContainingNamespace ?? string.Empty, y.ContainingNamespace ?? string.Empty);
        if (byNamespace != 0)
        {
            return byNamespace;
        }

        var byType = StringComparer.Ordinal.Compare(x.ContainingType ?? string.Empty, y.ContainingType ?? string.Empty);
        if (byType != 0)
        {
            return byType;
        }

        var byDeclaration = SourceLocationComparer.Instance.Compare(x.DeclarationLocation, y.DeclarationLocation);
        if (byDeclaration != 0)
        {
            return byDeclaration;
        }

        return StringComparer.Ordinal.Compare(x.SymbolId, y.SymbolId);
    }
}

internal sealed class CallEdgeComparer : IComparer<CallEdge>
{
    public static readonly CallEdgeComparer Instance = new();

    public int Compare(CallEdge? x, CallEdge? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var byFrom = StringComparer.Ordinal.Compare(x.FromSymbolId, y.FromSymbolId);
        if (byFrom != 0)
        {
            return byFrom;
        }

        var byTo = StringComparer.Ordinal.Compare(x.ToSymbolId, y.ToSymbolId);
        if (byTo != 0)
        {
            return byTo;
        }

        return SourceLocationComparer.Instance.Compare(x.Location, y.Location);
    }
}
