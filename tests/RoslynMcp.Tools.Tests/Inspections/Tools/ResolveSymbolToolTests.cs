using Is.Assertions;
using RoslynMcp.Tools.Inspection.ResolveSymbol;
using RoslynMcp.Tools.Tests.Inspections;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Tests.Inspections.Tools;

[Collection(CurrentDirectorySensitiveCollection.Name)]
public sealed class ResolveSymbolToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<Tool>(fixture, output), IClassFixture<SharedSandboxFixture>
{
    [Fact]
    public async Task Run_WithWorkspaceRelativeSourcePosition_ReturnsRelativeLocation()
    {
        await using var context = await WorkspaceRootSandboxContext.CreateAsync();
        var sut = context.GetRequiredService<Tool>();

        var result = await sut.Run(CancellationToken.None, path: Path.Combine("ProjectApp", "AppOrchestrator.cs"), line: 6, column: 21);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.Symbol!.Location!.FilePath.Is(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
    }

    [Fact]
    public async Task Run_WithQualifiedName_ReturnsResolvedTypeSymbol()
    {
        var result = await Sut.Run(CancellationToken.None, qualifiedName: "ProjectApp.AppOrchestrator", projectName: "ProjectApp");

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("AppOrchestrator", "NamedType", Path.Combine("ProjectApp", "AppOrchestrator.cs"));
    }

    [Fact]
    public async Task Run_WithQualifiedNameWithoutProjectScope_ReturnsResolvedTypeSymbol()
    {
        var result = await Sut.Run(CancellationToken.None, qualifiedName: "ProjectApp.AppOrchestrator");

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("AppOrchestrator", "NamedType", Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.Symbol!.QualifiedDisplayName.IsNull();
        result.Symbol.SymbolId.ShouldNotBeEmpty();
        result.Symbol.Location.IsNotNull();
        result.Symbol.Location!.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.Symbol.Location.Line.Is(6);
    }

    [Fact]
    public async Task Run_WithGenericQualifiedName_ReturnsResolvedGenericTypeSymbol()
    {
        var result = await Sut.Run(CancellationToken.None, qualifiedName: "ProjectCore.OperationBase<TInput>", projectName: "ProjectCore");

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("OperationBase<TInput>", "NamedType", Path.Combine("ProjectCore", "Contracts.cs"));
        result.Symbol!.QualifiedDisplayName.IsNull();
    }

    [Fact]
    public async Task Run_WithQualifiedMemberSignature_ReturnsResolvedMethodSymbol()
    {
        var result = await Sut.Run(
            CancellationToken.None,
            qualifiedName: "ProjectImpl.FastWorkItemOperation.ExecuteAsync(Guid, string, CancellationToken)",
            projectName: "ProjectImpl");

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedMember("ExecuteAsync", "Method", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 27);
        result.Symbol!.QualifiedDisplayName.IsNull();
    }

    [Fact]
    public async Task Run_WithSourcePosition_ReturnsResolvedTypeSymbol()
    {
        var result = await Sut.Run(CancellationToken.None, path: AppOrchestratorPath, line: 6, column: 21);

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("AppOrchestrator", "NamedType", Path.Combine("ProjectApp", "AppOrchestrator.cs"));
    }

    [Fact]
    public async Task Run_WithSourcePositionOnMethodDeclaration_ReturnsResolvedMethodSymbol()
    {
        var result = await Sut.Run(CancellationToken.None, path: AppOrchestratorPath, line: 54, column: 35);

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedMember("ExecuteFlowAsync", "Method", Path.Combine("ProjectApp", "AppOrchestrator.cs"), 54);
    }

    [Fact]
    public async Task Run_WithSourcePositionOnMethodCallSite_ReturnsResolvedMethodSymbol()
    {
        var result = await Sut.Run(CancellationToken.None, path: AppOrchestratorPath, line: 23, column: 34);

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedMember("ExecuteFlowAsync", "Method", Path.Combine("ProjectApp", "AppOrchestrator.cs"), 54);
    }

    [Fact]
    public async Task Run_WithSymbolIdRoundtrip_ReturnsSameSymbol()
    {
        var initial = await Sut.Run(CancellationToken.None, qualifiedName: "ProjectApp.AppOrchestrator", projectName: "ProjectApp");

        initial.Error.IsNull();
        initial.Symbol.ShouldMatchResolvedSymbol("AppOrchestrator", "NamedType", Path.Combine("ProjectApp", "AppOrchestrator.cs"));

        var roundtrip = await Sut.Run(CancellationToken.None, symbolId: initial.Symbol!.SymbolId);

        roundtrip.Error.IsNull();
        roundtrip.IsAmbiguous.IsFalse();
        roundtrip.Candidates.IsEmpty();
        roundtrip.Symbol.ShouldMatchResolvedSymbol("AppOrchestrator", "NamedType", Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        roundtrip.Symbol!.SymbolId.Is(initial.Symbol.SymbolId);
        roundtrip.Symbol.Location.IsNotNull();
        initial.Symbol.Location.IsNotNull();
        roundtrip.Symbol.Location!.FilePath.Is(initial.Symbol.Location!.FilePath);
    }

    [Fact]
    public async Task Run_WithDuplicateProjectViews_ReturnsCanonicalResolvedSymbol()
    {
        var result = await Sut.Run(CancellationToken.None, qualifiedName: "ProjectImpl.FastWorkItemOperation");

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("FastWorkItemOperation", "NamedType", Path.Combine("ProjectImpl", "WorkItemOperations.cs"));
    }

    [Fact]
    public async Task Run_WithShortMemberName_ReturnsResolvedMethodSymbol()
    {
        var result = await Sut.Run(CancellationToken.None, qualifiedName: "RunReflectionPathAsync");

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedMember("RunReflectionPathAsync", "Method", Path.Combine("ProjectApp", "AppOrchestrator.cs"), 34);
    }

    [Fact]
    public async Task Run_WithShortNameAndDuplicateProjectViews_ReturnsCanonicalResolvedSymbol()
    {
        var result = await Sut.Run(CancellationToken.None, qualifiedName: "FastWorkItemOperation");

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("FastWorkItemOperation", "NamedType", Path.Combine("ProjectImpl", "WorkItemOperations.cs"));
    }

    [Fact]
    public async Task Run_WithInvalidQualifiedName_ReturnsSymbolNotFound()
    {
        var result = await Sut.Run(CancellationToken.None, qualifiedName: "ProjectApp.DoesNotExist", projectName: "ProjectApp");

        result.Error.IsNotNull();
        result.Error!.Code.Is("symbol_not_found");
        result.IsAmbiguous.IsFalse();
        result.Symbol.IsNull();
        result.Candidates.IsEmpty();
    }

    [Fact]
    public async Task Run_WithInvalidSourcePosition_ReturnsSymbolNotFound()
    {
        var result = await Sut.Run(CancellationToken.None, path: AppOrchestratorPath, line: 999, column: 1);

        result.Error.IsNotNull();
        result.Error!.Code.Is("symbol_not_found");
        result.IsAmbiguous.IsFalse();
        result.Symbol.IsNull();
        result.Candidates.IsEmpty();
    }

    [Fact]
    public async Task Run_WithProjectScope_DisambiguatesQualifiedName()
    {
        var result = await Sut.Run(CancellationToken.None, qualifiedName: "ProjectImpl.FastWorkItemOperation", projectName: "ProjectImpl");

        result.Error.IsNull();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("FastWorkItemOperation", "NamedType", Path.Combine("ProjectImpl", "WorkItemOperations.cs"));
    }

    [Fact]
    public async Task Run_WithAmbiguousQualifiedMemberName_ReturnsStructuredCandidates()
    {
        var result = await Sut.Run(CancellationToken.None, qualifiedName: "ProjectImpl.FastWorkItemOperation.ExecuteAsync", projectName: "ProjectImpl");

        result.Error.IsNotNull();
        result.Error!.Code.Is("ambiguous_symbol");
        result.Symbol.IsNull();
        result.IsAmbiguous.IsTrue();
        result.Candidates.Count.Is(3);
        result.Candidates.All(static candidate => !string.IsNullOrWhiteSpace(candidate.SymbolId)).IsTrue();
        result.Candidates.ShouldContainCandidate("ProjectImpl.FastWorkItemOperation.ExecuteAsync(WorkItem, CancellationToken)", "ProjectImpl");
        result.Candidates.ShouldContainCandidate("ProjectImpl.FastWorkItemOperation.ExecuteAsync(Guid, string, CancellationToken)", "ProjectImpl");
        result.Candidates.ShouldContainCandidate("ProjectImpl.FastWorkItemOperation.ExecuteAsync(Guid, string, int, CancellationToken)", "ProjectImpl");
        result.Candidates.All(static candidate => candidate.QualifiedDisplayName is not null).IsTrue();
    }

    [Fact]
    public async Task Run_WhenNoSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.Run(CancellationToken.None);

        result.Error.IsNotNull();
        result.Error!.Code.Is("invalid_input");
    }

    [Fact]
    public async Task Run_NonAmbiguousSerialization_OmitsReferenceBallast()
    {
        var result = await Sut.Run(CancellationToken.None, qualifiedName: "ProjectApp.AppOrchestrator");

        result.Error.IsNull();

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        json.Contains("reference", StringComparison.Ordinal).IsFalse();
        json.Contains("qualifiedDisplayName", StringComparison.Ordinal).IsFalse();
    }
}

file sealed class WorkspaceRootSandboxContext : SandboxContext
{
    public static async Task<WorkspaceRootSandboxContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        var context = new WorkspaceRootSandboxContext();
        try
        {
            var sandbox = TestSolutionSandbox.Create(context.CanonicalTestSolutionDirectory);
            using var currentDirectory = new CurrentDirectoryScope(sandbox.SolutionRoot);
            await context.InitializeSandboxAsync(sandbox, cancellationToken).ConfigureAwait(false);
            return context;
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

file sealed class CurrentDirectoryScope : IDisposable
{
    private readonly string _originalDirectory;

    public CurrentDirectoryScope(string currentDirectory)
    {
        _originalDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(currentDirectory);
    }

    public void Dispose()
        => Directory.SetCurrentDirectory(Directory.Exists(_originalDirectory) ? _originalDirectory : AppContext.BaseDirectory);
}

file static class AssertionExtensions
{
    internal static void ShouldMatchResolvedSymbol(this ResolvedSymbol? symbol, string expectedDisplayName, string expectedKind, string expectedFileName)
    {
        symbol.IsNotNull();
        symbol!.DisplayName.Is(expectedDisplayName);
        symbol.Kind.Is(expectedKind);
        symbol.Location.IsNotNull();
        symbol.Location!.FilePath.ShouldEndWithPathSuffix(expectedFileName);
        symbol.SymbolId.ShouldNotBeEmpty();
    }

    internal static void ShouldMatchResolvedMember(this ResolvedSymbol? symbol, string expectedName, string expectedKind, string expectedFileName, int expectedLine)
    {
        symbol.IsNotNull();
        symbol!.DisplayName.Contains(expectedName, StringComparison.Ordinal).IsTrue();
        symbol.Kind.Is(expectedKind);
        symbol.Location.IsNotNull();
        symbol.Location!.FilePath.ShouldEndWithPathSuffix(expectedFileName);
        symbol.Location.Line.Is(expectedLine);
        symbol.SymbolId.ShouldNotBeEmpty();
    }

    internal static void ShouldContainCandidate(this IReadOnlyList<Candidate> candidates, string expectedQualifiedDisplayName, string expectedProjectName)
    {
        candidates.Any(candidate =>
                string.Equals(candidate.QualifiedDisplayName, expectedQualifiedDisplayName, StringComparison.Ordinal)
                && string.Equals(candidate.ProjectName, expectedProjectName, StringComparison.Ordinal))
            .IsTrue();
    }
}
