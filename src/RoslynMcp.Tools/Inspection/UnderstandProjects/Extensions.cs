using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Tools.Infrastructure;

namespace RoslynMcp.Tools.Inspection.UnderstandProjects;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddUnderstandProjectsTool() => services
            .AddSingleton<Service>()
            .AddSingleton<Tool>();
    }

    extension(string? profile)
    {
        public Request ToRequest() => new(profile.NormalizeProfile());

        private string NormalizeProfile()
            => string.IsNullOrWhiteSpace(profile)
                ? Profiles.Standard
                : profile.Trim().ToLowerInvariant() switch
                {
                    Profiles.Quick => Profiles.Quick,
                    Profiles.Deep => Profiles.Deep,
                    _ => Profiles.Standard
                };
    }

    extension(ProjectSummary project)
    {
        public ProjectSummary WithWorkspaceRelativePaths()
            => project with
            {
                ProjectPath = project.ProjectPath?.ToWorkspaceRelativePathIfPossible(),
                OutgoingDependencyProjectPaths = [.. project.OutgoingDependencyProjectPaths.Select(path => path.ToWorkspaceRelativePathIfPossible())],
                IncomingDependencyProjectPaths = [.. project.IncomingDependencyProjectPaths.Select(path => path.ToWorkspaceRelativePathIfPossible())]
            };
    }

    extension(SourceLocation? location)
    {
        public SourceLocation? WithWorkspaceRelativePaths()
            => location is null ? null : location with { FilePath = location.FilePath.ToWorkspaceRelativePathIfPossible() };
    }

    extension(Hotspot hotspot)
    {
        public Hotspot WithWorkspaceRelativePaths()
            => hotspot with { Location = hotspot.Location.WithWorkspaceRelativePaths() };
    }

    extension(ErrorInfo? error)
    {
        public ErrorInfo? WithWorkspaceRelativePaths()
        {
            if (error?.Details is null || error.Details.Count == 0)
                return error;

            Dictionary<string, string>? updated = null;
            foreach (var pair in error.Details)
            {
                if (!PathDetailKeys.Contains(pair.Key))
                    continue;

                var outward = pair.Value.ToWorkspaceRelativePathIfPossible();
                if (string.Equals(outward, pair.Value, StringComparison.Ordinal))
                    continue;

                updated ??= new Dictionary<string, string>(error.Details, StringComparer.Ordinal);
                updated[pair.Key] = outward;
            }

            return updated is null ? error : error with { Details = updated };
        }
    }

    extension(Result result)
    {
        public Result WithWorkspaceRelativePaths()
            => result with
            {
                Projects = [.. result.Projects.Select(project => project.WithWorkspaceRelativePaths())],
                Hotspots = [.. result.Hotspots.Select(hotspot => hotspot.WithWorkspaceRelativePaths())],
                Error = result.Error.WithWorkspaceRelativePaths()
            };
    }

    public static IEnumerable<INamedTypeSymbol> EnumerateTypes(this INamespaceSymbol root)
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

    public static (string FilePath, int? Line, int? Column) GetDeclarationPosition(this ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
        if (location is null)
            return (string.Empty, null, null);

        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        return (span.Path ?? string.Empty, start.Line + 1, start.Character + 1);
    }

    public static string ToQualifiedDisplayName(this INamedTypeSymbol symbol)
        => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    public static string ToStableId(this ISymbol symbol)
        => $"{symbol.Kind}:{symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}";

    private static readonly HashSet<string> PathDetailKeys =
    [
        "path",
        "filepath",
        "projectpath",
        "workspaceRoot"
    ];

    internal static class Profiles
    {
        public const string Quick = "quick";
        public const string Standard = "standard";
        public const string Deep = "deep";
    }
}
