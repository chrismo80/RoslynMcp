using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class ListMembersToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<ListMembersTool>(fixture, output)
{
    [Fact]
    public async Task ListMembersAsync_WithTypeSymbolIdAndMethodFilter_ReturnsOrderedMethods()
    {
        var appOrchestratorSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");

        var result = await Sut.ExecuteAsync(CancellationToken.None, typeSymbolId: appOrchestratorSymbolId, kind: "method");

        result.Error.ShouldBeNone();
        result.IncludeInherited.Is(false);
        result.TotalCount.Is(5);
        ShouldMatchMembers(result.Members,
            ("ExecuteFlowAsync", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 53),
            ("OnStateChanged", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 66),
            ("OnStepCompleted", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 58),
            ("RunAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 15),
            ("RunReflectionPathAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 33));
    }

    [Fact]
    public async Task ListMembersAsync_WithSourcePositionSelector_ReturnsContainingTypeFields()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 53, column: 35, kind: "field");

        result.Error.ShouldBeNone();
        result.IncludeInherited.Is(false);
        result.TotalCount.Is(5);
        ShouldMatchMembers(result.Members,
            ("SampleId", "field", "private", true, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 8),
            ("_operation", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 10),
            ("_session", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 11),
            ("_smells", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 12),
            ("_steps", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 13));
    }

    [Fact]
    public async Task ListMembersAsync_WithAccessibilityFilter_ReturnsOnlyPublicMethods()
    {
        var appOrchestratorSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");

        var result = await Sut.ExecuteAsync(
            CancellationToken.None,
            typeSymbolId: appOrchestratorSymbolId,
            kind: "method",
            accessibility: "public");

        result.Error.ShouldBeNone();
        result.TotalCount.Is(2);
        ShouldMatchMembers(result.Members,
            ("RunAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 15),
            ("RunReflectionPathAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 33));
    }

    [Fact]
    public async Task ListMembersAsync_WithBindingFilter_ReturnsOnlyInstanceFields()
    {
        var appOrchestratorSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");

        var result = await Sut.ExecuteAsync(
            CancellationToken.None,
            typeSymbolId: appOrchestratorSymbolId,
            kind: "field",
            binding: "instance");

        result.Error.ShouldBeNone();
        result.TotalCount.Is(4);
        result.Members.All(static member => !member.IsStatic).IsTrue();
        ShouldMatchMembers(result.Members,
            ("_operation", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 10),
            ("_session", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 11),
            ("_smells", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 12),
            ("_steps", "field", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 13));
    }

    [Fact]
    public async Task ListMembersAsync_WithIncludeInherited_TogglesInheritedEvents()
    {
        var fastOperationSymbolId = await GetTypeSymbolIdAsync("ProjectImpl", "FastWorkItemOperation");

        var directOnly = await Sut.ExecuteAsync(
            CancellationToken.None,
            typeSymbolId: fastOperationSymbolId,
            kind: "event",
            includeInherited: false);

        directOnly.Error.ShouldBeNone();
        directOnly.IncludeInherited.Is(false);
        directOnly.TotalCount.Is(0);
        directOnly.Members.IsEmpty();

        var withInherited = await Sut.ExecuteAsync(
            CancellationToken.None,
            typeSymbolId: fastOperationSymbolId,
            kind: "event",
            includeInherited: true);

        withInherited.Error.ShouldBeNone();
        withInherited.IncludeInherited.Is(true);
        withInherited.TotalCount.Is(3);
        withInherited.Members.Count(static member => member.DisplayName == "StepCompleted").Is(2);
        withInherited.Members.All(static member => member is { Kind: "event", Accessibility: "public", IsStatic: false }).IsTrue();
        withInherited.Members
            .Select(static member => $"{member.DisplayName}@{member.Line}")
            .OrderBy(static member => member, StringComparer.Ordinal)
            .ToArray()
            .Is("Logged@39", "StepCompleted@23", "StepCompleted@37");
    }

    [Fact]
    public async Task ListMembersAsync_WithLimitAndOffset_ReturnsDeterministicPage()
    {
        var appOrchestratorSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");

        var fullResult = await Sut.ExecuteAsync(CancellationToken.None, typeSymbolId: appOrchestratorSymbolId, kind: "method");

        fullResult.Error.ShouldBeNone();
        fullResult.TotalCount.Is(5);

        var pagedResult = await Sut.ExecuteAsync(
            CancellationToken.None,
            typeSymbolId: appOrchestratorSymbolId,
            kind: "method",
            limit: 2,
            offset: 1);

        pagedResult.Error.ShouldBeNone();
        pagedResult.TotalCount.Is(5);

        ShouldMatchMembers(pagedResult.Members,
            ("OnStateChanged", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 66),
            ("OnStepCompleted", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 58));

        pagedResult.Members.Select(static member => member.DisplayName)
            .Is(fullResult.Members.Skip(1).Take(2).Select(static member => member.DisplayName));
    }

    [Fact]
    public async Task ListMembersAsync_WithInvalidKind_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, typeSymbolId: "ProjectApp|type|AppOrchestrator", kind: "invalid");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task ListMembersAsync_WithInvalidBinding_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, typeSymbolId: "ProjectApp|type|AppOrchestrator", binding: "invalid");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    private async Task<string> GetTypeSymbolIdAsync(string projectName, string typeDisplayName)
    {
        var listTypes = Context.GetRequiredService<ListTypesTool>();
        var typeResult = await listTypes.ExecuteAsync(CancellationToken.None, projectName: projectName);

        typeResult.Error.ShouldBeNone();

        return typeResult.Types.Single(type => type.DisplayName == typeDisplayName).SymbolId;
    }

    private static void ShouldMatchMembers(IReadOnlyList<MemberListEntry> actual,
        params (string DisplayName, string Kind, string Accessibility, bool IsStatic, string FileName, int Line)[] expected)
    {
        actual.Count.Is(expected.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            actual[i].DisplayName.Is(expected[i].DisplayName);
            actual[i].Kind.Is(expected[i].Kind);
            actual[i].Accessibility.Is(expected[i].Accessibility);
            actual[i].IsStatic.Is(expected[i].IsStatic);
            actual[i].FilePath.ShouldEndWithPathSuffix(expected[i].FileName);
            actual[i].Line.Is(expected[i].Line);
            actual[i].SymbolId.ShouldNotBeEmpty();
        }
    }
}