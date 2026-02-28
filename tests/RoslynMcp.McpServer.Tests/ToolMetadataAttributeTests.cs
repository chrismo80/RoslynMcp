using RoslynMcp.McpServer.Tools;
using Is.Assertions;
using System.ComponentModel;
using System.Reflection;

namespace RoslynMcp.McpServer.Tests;

public sealed class ToolMetadataAttributeTests
{
    [Theory]
    [InlineData(typeof(LoadSolutionTools), nameof(LoadSolutionTools.LoadSolutionAsync), "load_solution")]
    [InlineData(typeof(UnderstandCodebaseTools), nameof(UnderstandCodebaseTools.UnderstandCodebaseAsync), "understand_codebase")]
    [InlineData(typeof(ListTypesTools), nameof(ListTypesTools.ListTypesAsync), "list_types")]
    [InlineData(typeof(ListMembersTools), nameof(ListMembersTools.ListMembersAsync), "list_members")]
    [InlineData(typeof(ResolveSymbolTools), nameof(ResolveSymbolTools.ResolveSymbolAsync), "resolve_symbol")]
    [InlineData(typeof(ExplainSymbolTools), nameof(ExplainSymbolTools.ExplainSymbolAsync), "explain_symbol")]
    [InlineData(typeof(TraceFlowTools), nameof(TraceFlowTools.TraceFlowAsync), "trace_flow")]
    [InlineData(typeof(CodeSmellTools), nameof(CodeSmellTools.FindCodeSmellsAsync), "find_codesmells")]
    public void IntentEndpoints_ArePublishedWithExpectedNames(Type toolType, string methodName, string expectedName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        method.IsNotNull();

        var attribute = GetToolAttribute(method!);
        attribute.IsNotNull();

        var property = attribute!.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        property.IsNotNull();
        property!.GetValue(attribute).Is(expectedName);
    }

    [Theory]
    [InlineData(typeof(LoadSolutionTools), nameof(LoadSolutionTools.LoadSolutionAsync))]
    [InlineData(typeof(UnderstandCodebaseTools), nameof(UnderstandCodebaseTools.UnderstandCodebaseAsync))]
    [InlineData(typeof(ListTypesTools), nameof(ListTypesTools.ListTypesAsync))]
    [InlineData(typeof(ListMembersTools), nameof(ListMembersTools.ListMembersAsync))]
    [InlineData(typeof(ResolveSymbolTools), nameof(ResolveSymbolTools.ResolveSymbolAsync))]
    [InlineData(typeof(ExplainSymbolTools), nameof(ExplainSymbolTools.ExplainSymbolAsync))]
    [InlineData(typeof(TraceFlowTools), nameof(TraceFlowTools.TraceFlowAsync))]
    [InlineData(typeof(CodeSmellTools), nameof(CodeSmellTools.FindCodeSmellsAsync))]
    public void IntentEndpoints_HaveMethodDescription(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        method.IsNotNull();

        var description = method!.GetCustomAttribute<DescriptionAttribute>(inherit: false);
        description.IsNotNull();
        string.IsNullOrWhiteSpace(description!.Description).IsFalse();
    }

    [Theory]
    [InlineData(typeof(LoadSolutionTools), nameof(LoadSolutionTools.LoadSolutionAsync))]
    [InlineData(typeof(UnderstandCodebaseTools), nameof(UnderstandCodebaseTools.UnderstandCodebaseAsync))]
    [InlineData(typeof(ListTypesTools), nameof(ListTypesTools.ListTypesAsync))]
    [InlineData(typeof(ListMembersTools), nameof(ListMembersTools.ListMembersAsync))]
    [InlineData(typeof(ResolveSymbolTools), nameof(ResolveSymbolTools.ResolveSymbolAsync))]
    [InlineData(typeof(ExplainSymbolTools), nameof(ExplainSymbolTools.ExplainSymbolAsync))]
    [InlineData(typeof(TraceFlowTools), nameof(TraceFlowTools.TraceFlowAsync))]
    [InlineData(typeof(CodeSmellTools), nameof(CodeSmellTools.FindCodeSmellsAsync))]
    public void IntentEndpoints_HaveParameterDescriptions(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        method.IsNotNull();

        foreach (var parameter in method!.GetParameters().Where(static p => p.ParameterType != typeof(CancellationToken)))
        {
            var description = parameter.GetCustomAttribute<DescriptionAttribute>(inherit: false);
            description.IsNotNull();
            string.IsNullOrWhiteSpace(description!.Description).IsFalse();
        }
    }

    [Fact]
    public void IntentEndpoints_OptionalParametersHaveClrDefaultValues()
    {
        var expectations = new Dictionary<(Type ToolType, string MethodName), string[]>
        {
            [(typeof(LoadSolutionTools), nameof(LoadSolutionTools.LoadSolutionAsync))] = ["solutionHintPath"],
            [(typeof(UnderstandCodebaseTools), nameof(UnderstandCodebaseTools.UnderstandCodebaseAsync))] = ["profile"],
            [(typeof(ListTypesTools), nameof(ListTypesTools.ListTypesAsync))] = ["projectPath", "projectName", "projectId", "namespacePrefix", "kind", "accessibility", "limit", "offset"],
            [(typeof(ListMembersTools), nameof(ListMembersTools.ListMembersAsync))] = ["typeSymbolId", "path", "line", "column", "kind", "accessibility", "binding", "includeInherited", "limit", "offset"],
            [(typeof(ResolveSymbolTools), nameof(ResolveSymbolTools.ResolveSymbolAsync))] = ["symbolId", "path", "line", "column", "qualifiedName", "projectPath", "projectName", "projectId"],
            [(typeof(ExplainSymbolTools), nameof(ExplainSymbolTools.ExplainSymbolAsync))] = ["symbolId", "path", "line", "column"],
            [(typeof(TraceFlowTools), nameof(TraceFlowTools.TraceFlowAsync))] = ["symbolId", "path", "line", "column", "direction", "depth"],
            [(typeof(CodeSmellTools), nameof(CodeSmellTools.FindCodeSmellsAsync))] = [],
        };

        foreach (var ((toolType, methodName), optionalParameterNames) in expectations)
        {
            var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            method.IsNotNull();

            var optionalNames = optionalParameterNames.ToHashSet(StringComparer.Ordinal);
            foreach (var parameter in method!.GetParameters())
            {
                var isExpectedOptional = optionalNames.Contains(parameter.Name ?? string.Empty);
                parameter.IsOptional.Is(isExpectedOptional);
                parameter.HasDefaultValue.Is(isExpectedOptional);

                if (!isExpectedOptional)
                {
                    continue;
                }

                if (parameter.ParameterType == typeof(bool))
                {
                    if (string.Equals(parameter.Name, "failOnErrors", StringComparison.Ordinal))
                    {
                        parameter.DefaultValue.Is(true);
                        continue;
                    }

                    parameter.DefaultValue.Is(false);
                    continue;
                }

                parameter.DefaultValue.IsNull();
            }
        }
    }

    private static object? GetToolAttribute(MethodInfo method)
        => method
            .GetCustomAttributes(inherit: false)
            .SingleOrDefault(static attr => string.Equals(attr.GetType().Name, "McpServerToolAttribute", StringComparison.Ordinal));
}