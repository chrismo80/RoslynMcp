using Is.Assertions;
using RoslynMcp.Tools.Tests.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

[Collection(SharedSandboxCollections.CoreCollectionName)]
public sealed class ListMembersToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<RoslynMcp.Tools.Inspection.ListMembers.Tool>(fixture, output), IClassFixture<SharedSandboxFixture>
{
    [Fact]
    public async Task Run_WithTypeSymbolIdAndMethodFilter_ReturnsOrderedMethods()
    {
        var appOrchestratorSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");
        var result = await Sut.Run(CancellationToken.None, typeSymbolId: appOrchestratorSymbolId, kind: "method");

        result.Error.IsNull();
        result.IncludeInherited.Is(false);
        result.TotalCount.Is(5);
        result.Members.ShouldMatchMembers(
            ("ExecuteFlowAsync", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 54),
            ("OnStateChanged", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 67),
            ("OnStepCompleted", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 59),
            ("RunAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 15),
            ("RunReflectionPathAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 34));
    }

    [Fact]
    public async Task Run_WithSourcePositionSelector_ReturnsContainingTypeFields()
    {
        var result = await Sut.Run(CancellationToken.None, path: AppOrchestratorPath, line: 54, column: 35, kind: "field");

        result.Error.IsNull();
        result.IncludeInherited.Is(false);
        result.TotalCount.Is(5);
        result.Members.ShouldMatchMembers(
            ("SampleId", "field", "private", true, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 8),
            ("_operation", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 10),
            ("_session", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 11),
            ("_smells", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 12),
            ("_steps", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 13));
    }

    [Fact]
    public async Task Run_WithAccessibilityFilter_ReturnsOnlyPublicMethods()
    {
        var appOrchestratorSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");
        var result = await Sut.Run(CancellationToken.None, typeSymbolId: appOrchestratorSymbolId, kind: "method", accessibility: "public");

        result.Error.IsNull();
        result.TotalCount.Is(2);
        result.Members.ShouldMatchMembers(
            ("RunAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 15),
            ("RunReflectionPathAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 34));
    }

    [Fact]
    public async Task Run_WithBindingFilter_ReturnsOnlyInstanceFields()
    {
        var appOrchestratorSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");
        var result = await Sut.Run(CancellationToken.None, typeSymbolId: appOrchestratorSymbolId, kind: "field", binding: "instance");

        result.Error.IsNull();
        result.TotalCount.Is(4);
        result.Members.All(static member => !member.IsStatic).IsTrue();
        result.Members.ShouldMatchMembers(
            ("_operation", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 10),
            ("_session", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 11),
            ("_smells", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 12),
            ("_steps", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 13));
    }

    [Fact]
    public async Task Run_WithIncludeInherited_TogglesInheritedEvents()
    {
        var fastOperationSymbolId = await GetTypeSymbolIdAsync("ProjectImpl", "FastWorkItemOperation");

        var directOnly = await Sut.Run(CancellationToken.None, typeSymbolId: fastOperationSymbolId, kind: "event", includeInherited: false);
        directOnly.Error.IsNull();
        directOnly.IncludeInherited.Is(false);
        directOnly.TotalCount.Is(0);
        directOnly.Members.IsEmpty();

        var withInherited = await Sut.Run(CancellationToken.None, typeSymbolId: fastOperationSymbolId, kind: "event", includeInherited: true);
        withInherited.Error.IsNull();
        withInherited.IncludeInherited.Is(true);
        withInherited.TotalCount.Is(3);
        withInherited.Members.Count(static member => member.DisplayName == "StepCompleted").Is(2);
        withInherited.Members.All(static member => member is { Kind: "event", Accessibility: "public", IsStatic: false }).IsTrue();
        withInherited.Members.Select(static member => $"{member.DisplayName}@{member.Location!.Line}").OrderBy(static member => member, StringComparer.Ordinal).ToArray().Is("Logged@39", "StepCompleted@23", "StepCompleted@37");
    }

    [Fact]
    public async Task Run_UsesCompactLocationBasedEntries()
    {
        var appOrchestratorSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");
        var members = await Sut.Run(CancellationToken.None, typeSymbolId: appOrchestratorSymbolId, kind: "method");

        members.Error.IsNull();
        var listed = members.Members.Single(member => member.DisplayName == "RunReflectionPathAsync");
        listed.Location.IsNotNull();
        listed.Location!.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        listed.Location.Line.Is(34);
    }

    [Fact]
    public async Task Run_WithLimitAndOffset_ReturnsDeterministicPage()
    {
        var appOrchestratorSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");
        var fullResult = await Sut.Run(CancellationToken.None, typeSymbolId: appOrchestratorSymbolId, kind: "method");

        fullResult.Error.IsNull();
        fullResult.TotalCount.Is(5);

        var pagedResult = await Sut.Run(CancellationToken.None, typeSymbolId: appOrchestratorSymbolId, kind: "method", limit: 2, offset: 1);

        pagedResult.Error.IsNull();
        pagedResult.TotalCount.Is(5);
        pagedResult.Members.ShouldMatchMembers(
            ("OnStateChanged", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 67),
            ("OnStepCompleted", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 59));
        pagedResult.Members.Select(static member => member.DisplayName).Is(fullResult.Members.Skip(1).Take(2).Select(static member => member.DisplayName));
    }

    [Fact]
    public async Task Run_WithInvalidKind_ReturnsValidationError()
    {
        var result = await Sut.Run(CancellationToken.None, typeSymbolId: "ProjectApp|type|AppOrchestrator", kind: "invalid");
        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_input");
    }

    [Fact]
    public async Task Run_WithInvalidBinding_ReturnsValidationError()
    {
        var result = await Sut.Run(CancellationToken.None, typeSymbolId: "ProjectApp|type|AppOrchestrator", binding: "invalid");
        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_input");
    }

    private async Task<string> GetTypeSymbolIdAsync(string projectName, string typeDisplayName)
    {
        var listTypes = Context.GetRequiredService<RoslynMcp.Tools.Inspection.ListTypes.Tool>();
        var typeResult = await listTypes.Run(CancellationToken.None, projectName: projectName);

        typeResult.Error.IsNull();
        return typeResult.Types.Single(type => type.DisplayName == typeDisplayName).SymbolId;
    }
}

file static class AssertionExtensions
{
    internal static void ShouldMatchMembers(this IReadOnlyList<RoslynMcp.Tools.Inspection.ListMembers.Entry> actual, params (string DisplayName, string Kind, string Accessibility, bool IsStatic, string FileName, int Line)[] expected)
    {
        actual.Count.Is(expected.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            actual[i].DisplayName.Is(expected[i].DisplayName);
            actual[i].Kind.Is(expected[i].Kind);
            actual[i].Accessibility.Is(expected[i].Accessibility);
            actual[i].IsStatic.Is(expected[i].IsStatic);
            actual[i].Location.IsNotNull();
            actual[i].Location!.FilePath.ShouldEndWithPathSuffix(expected[i].FileName);
            actual[i].Location.Line.Is(expected[i].Line);
            actual[i].SymbolId.ShouldNotBeEmpty();
        }
    }
}
