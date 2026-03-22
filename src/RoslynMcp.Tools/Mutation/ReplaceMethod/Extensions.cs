using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Mutation.ReplaceMethod;

internal static class Extensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddReplaceMethodTool() => services.AddSingleton<Service>().AddSingleton<Tool>();
    }

    extension(string targetMethodSymbolId)
    {
        public Request ToRequest(string name, string returnType, string accessibility, IReadOnlyList<string>? modifiers, IReadOnlyList<string>? parameters, string body)
            => new(targetMethodSymbolId.Trim(), name.Trim(), returnType.Trim(), accessibility.Trim(), modifiers, parameters, body);
    }

    internal static MethodInsertionSpec ToSpec(this Request request)
        => RoslynMcp.Tools.Mutation.AddMethod.Extensions.ToSpec(new RoslynMcp.Tools.Mutation.AddMethod.Request(request.TargetMethodSymbolId, request.Name, request.ReturnType, request.Accessibility, request.Modifiers, request.Parameters, request.Body));

    internal static Result WithWorkspaceRelativePaths(this Result result)
        => result with
        {
            ChangedFiles = [.. result.ChangedFiles.Select(static path => path.ToWorkspaceRelativePathIfPossible())],
            DiagnosticsDelta = new DiagnosticsDeltaInfo([.. result.DiagnosticsDelta.NewErrors.Select(static item => item with { FilePath = item.FilePath.ToWorkspaceRelativePathIfPossible() })], [.. result.DiagnosticsDelta.NewWarnings.Select(static item => item with { FilePath = item.FilePath.ToWorkspaceRelativePathIfPossible() })])
        };
}