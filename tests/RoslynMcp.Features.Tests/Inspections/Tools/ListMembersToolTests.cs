using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
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
        result.Members.ShouldMatchMembers(
            ("ExecuteFlowAsync", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 54),
            ("OnStateChanged", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 67),
            ("OnStepCompleted", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 59),
            ("RunAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 15),
            ("RunReflectionPathAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 34));
    }

    [Fact]
    public async Task ListMembersAsync_WithSourcePositionSelector_ReturnsContainingTypeFields()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 54, column: 35, kind: "field");

        result.Error.ShouldBeNone();
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
        result.Members.ShouldMatchMembers(
            ("RunAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 15),
            ("RunReflectionPathAsync", "method", "public", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 34));
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
        result.Members.ShouldMatchMembers(
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
            .Select(static member => $"{member.DisplayName}@{member.Location!.Line}")
            .OrderBy(static member => member, StringComparer.Ordinal)
            .ToArray()
            .Is("Logged@39", "StepCompleted@23", "StepCompleted@37");
    }

    [Fact]
    public async Task ListMembersAsync_UsesCompactLocationBasedEntries()
    {
        var appOrchestratorSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");

        var members = await Sut.ExecuteAsync(CancellationToken.None, typeSymbolId: appOrchestratorSymbolId, kind: "method");

        members.Error.ShouldBeNone();

        var listed = members.Members.Single(member => member.DisplayName == "RunReflectionPathAsync");
        listed.Location.IsNotNull();
        listed.Location!.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        listed.Location.Line.Is(34);
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

        pagedResult.Members.ShouldMatchMembers(
            ("OnStateChanged", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 67),
            ("OnStepCompleted", "method", "private", false, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 59));

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
}

file static class AssertionExtensions
{
    extension(IReadOnlyList<MemberListEntry> actual)
    {
        internal void ShouldMatchMembers(params (string DisplayName, string Kind, string Accessibility, bool IsStatic, string FileName, int Line)[] expected)
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
                actual[i].SymbolId.ShouldBeExternalSymbolId();
            }
        }
    }
}
