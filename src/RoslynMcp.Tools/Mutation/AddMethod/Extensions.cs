using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Mutation.AddMethod;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddAddMethodTool() => services.AddSingleton<Service>().AddSingleton<Tool>();
    }

    extension(string targetTypeSymbolId)
    {
        public Request ToRequest(string name, string returnType, string accessibility, IReadOnlyList<string>? modifiers, IReadOnlyList<string>? parameters, string body)
            => new(targetTypeSymbolId.Trim(), name.Trim(), returnType.Trim(), accessibility.Trim(), modifiers, parameters, body);
    }

    internal static MethodInsertionSpec ToSpec(this Request request)
        => new(request.Name, request.ReturnType, request.Accessibility, request.Modifiers?.Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item.Trim()).ToArray() ?? [], request.Parameters?.Where(static item => !string.IsNullOrWhiteSpace(item)).Select(ParseParameter).ToArray() ?? [], request.Body.NormalizeEscapedNewlines());

    private static MethodParameterSpec ParseParameter(string parameter)
    {
        var value = parameter.Trim();
        var lastSpace = value.LastIndexOf(' ');
        return lastSpace <= 0 ? new MethodParameterSpec(value, string.Empty) : new MethodParameterSpec(value[(lastSpace + 1)..], value[..lastSpace].Trim());
    }

    internal static Result WithWorkspaceRelativePaths(this Result result)
        => result with
        {
            ChangedFiles = [.. result.ChangedFiles.Select(static path => path.ToWorkspaceRelativePathIfPossible())],
            DiagnosticsDelta = result.DiagnosticsDelta.WithWorkspaceRelativePaths()
        };

    private static DiagnosticsDeltaInfo WithWorkspaceRelativePaths(this DiagnosticsDeltaInfo diagnostics)
        => new([.. diagnostics.NewErrors.Select(static item => item with { FilePath = item.FilePath.ToWorkspaceRelativePathIfPossible() })], [.. diagnostics.NewWarnings.Select(static item => item with { FilePath = item.FilePath.ToWorkspaceRelativePathIfPossible() })]);
}