using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Infrastructure.Workspace;
using Is.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Infrastructure.Tests;

public sealed class RoslynNavigationServiceTests
{
    [Fact]
    public async Task FindSymbol_SearchSymbols_UsesCanonicalIdForFollowUpCalls()
    {
        var service = CreateService(CreateSampleSolution());
        var search = await service.SearchSymbolsAsync(new SearchSymbolsRequest("Use"), CancellationToken.None);
        var useMethod = search.Symbols.Single(s => s.Name == "Use");

        var find = await service.FindSymbolAsync(new FindSymbolRequest(useMethod.SymbolId), CancellationToken.None);

        search.Error.IsNull();
        find.Error.IsNull();
        find.Symbol?.SymbolId.Is(useMethod.SymbolId);
    }

    [Fact]
    public async Task SymbolAtPosition_ScopedSearch_AndSignature_WorkDeterministically()
    {
        var service = CreateService(CreateSampleSolution());
        var symbolAt = await service.GetSymbolAtPositionAsync(new GetSymbolAtPositionRequest("Implementation.cs", 24, 9), CancellationToken.None);
        var scoped = await service.SearchSymbolsScopedAsync(new SearchSymbolsScopedRequest("Run", SymbolSearchScopes.Project, "SampleProject", "Method", "Public", 10, 0), CancellationToken.None);
        var run = scoped.Symbols.Single(symbol => symbol.Name == "Run" && symbol.ContainingType?.Contains("IWorker", StringComparison.Ordinal) == true);
        var signature = await service.GetSignatureAsync(new GetSignatureRequest(run.SymbolId), CancellationToken.None);

        symbolAt.Error.IsNull();
        symbolAt.Symbol?.Name.Is("Helper");
        scoped.Error.IsNull();
        IsSorted(scoped.Symbols.Select(SymbolSortKey)).IsTrue();
        signature.Error.IsNull();
        (signature.Signature?.Contains("Run", StringComparison.Ordinal) ?? false).IsTrue();
    }

    [Fact]
    public async Task SearchSymbols_ReturnsDeterministicOrderingWithPagination()
    {
        var service = CreateService(CreateSampleSolution());

        var all = await service.SearchSymbolsAsync(new SearchSymbolsRequest("Run"), CancellationToken.None);
        var paged = await service.SearchSymbolsAsync(new SearchSymbolsRequest("Run", Limit: 1, Offset: 1), CancellationToken.None);

        all.Error.IsNull();
        (all.TotalCount >= 3).IsTrue();
        IsSorted(all.Symbols.Select(SymbolSortKey)).IsTrue();
        paged.Symbols.Single();
        paged.Symbols[0].SymbolId.Is(all.Symbols[1].SymbolId);
    }

    [Fact]
    public async Task FindReferences_ReturnsDeterministicDeduplicatedLocations()
    {
        var service = CreateService(CreateSampleSolution());
        var useMethod = await ResolveSymbolAsync(service, "Use", "Sample.Helper");

        var result = await service.FindReferencesAsync(new FindReferencesRequest(useMethod.SymbolId), CancellationToken.None);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        (result.References.Count >= 3).IsTrue();
        IsSorted(result.References, CompareLocation).IsTrue();
        result.References.Select(LocationSortKey).Distinct(StringComparer.Ordinal).Count().Is(result.References.Count);
    }

    [Fact]
    public async Task FindImplementations_ReturnsDeterministicImplementationSet()
    {
        var service = CreateService(CreateSampleSolution());
        var contractMethod = await ResolveSymbolAsync(service, "Run", "Sample.IWorker");

        var result = await service.FindImplementationsAsync(new FindImplementationsRequest(contractMethod.SymbolId), CancellationToken.None);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        result.Implementations.Count.Is(2);
        result.Implementations.Any(s => s.ContainingType?.Contains("WorkerA", StringComparison.Ordinal) == true).IsTrue();
        result.Implementations.Any(s => s.ContainingType?.Contains("WorkerB", StringComparison.Ordinal) == true).IsTrue();
        IsSorted(result.Implementations.Select(SymbolSortKey)).IsTrue();
    }

    [Fact]
    public async Task FindReferencesScoped_AppliesScopeFilteringAndStableTotalCount()
    {
        var service = CreateService(CreateSampleSolution());
        var useMethod = await ResolveSymbolAsync(service, "Use", "Sample.Helper");

        var solutionScope = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(useMethod.SymbolId, ReferenceScopes.Solution),
            CancellationToken.None);
        var projectScope = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(useMethod.SymbolId, ReferenceScopes.Project),
            CancellationToken.None);
        var documentScope = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(useMethod.SymbolId, ReferenceScopes.Document, "Implementation.cs"),
            CancellationToken.None);

        solutionScope.Error.IsNull();
        projectScope.Error.IsNull();
        documentScope.Error.IsNull();
        solutionScope.TotalCount.Is(solutionScope.References.Count);
        projectScope.TotalCount.Is(projectScope.References.Count);
        documentScope.TotalCount.Is(documentScope.References.Count);
        (solutionScope.TotalCount >= projectScope.TotalCount).IsTrue();
        (projectScope.TotalCount >= documentScope.TotalCount).IsTrue();
        IsSorted(solutionScope.References, CompareLocation).IsTrue();
        IsSorted(projectScope.References, CompareLocation).IsTrue();
        IsSorted(documentScope.References, CompareLocation).IsTrue();
        foreach (var location in documentScope.References)
        {
            location.FilePath.Is("Implementation.cs");
        }
    }

    [Fact]
    public async Task GetTypeHierarchy_ReturnsBaseInterfaceAndDerivedTypes()
    {
        var service = CreateService(CreateSampleSolution());
        var workerInterface = await ResolveSymbolAsync(service, "IWorker", string.Empty);
        var serviceType = await ResolveSymbolAsync(service, "Service", string.Empty);

        var interfaceHierarchy = await service.GetTypeHierarchyAsync(
            new GetTypeHierarchyRequest(workerInterface.SymbolId, IncludeTransitive: true, MaxDerived: 10),
            CancellationToken.None);
        var classHierarchy = await service.GetTypeHierarchyAsync(
            new GetTypeHierarchyRequest(serviceType.SymbolId, IncludeTransitive: false, MaxDerived: 10),
            CancellationToken.None);

        interfaceHierarchy.Error.IsNull();
        classHierarchy.Error.IsNull();
        interfaceHierarchy.DerivedTypes.Any(s => s.Name == "WorkerA").IsTrue();
        interfaceHierarchy.DerivedTypes.Any(s => s.Name == "WorkerB").IsTrue();
        IsSorted(interfaceHierarchy.DerivedTypes.Select(SymbolSortKey)).IsTrue();
        IsSorted(interfaceHierarchy.ImplementedInterfaces.Select(SymbolSortKey)).IsTrue();
        IsSorted(classHierarchy.BaseTypes.Select(SymbolSortKey)).IsTrue();
    }

    [Fact]
    public async Task GetSymbolOutline_ReturnsDeterministicCompactMembersAndAttributes()
    {
        var service = CreateService(CreateSampleSolution());
        var serviceType = await ResolveSymbolAsync(service, "Service", string.Empty);

        var result = await service.GetSymbolOutlineAsync(new GetSymbolOutlineRequest(serviceType.SymbolId, Depth: 5), CancellationToken.None);

        result.Error.IsNull();
        result.Symbol.IsNotNull();
        (result.Members.Count >= 1).IsTrue();
        IsSorted(result.Members, CompareOutlineMember).IsTrue();
        IsSorted(result.Attributes).IsTrue();
    }

    [Fact]
    public async Task GetCallers_And_GetCallees_ReturnDeterministicEdges()
    {
        var service = CreateService(CreateSampleSolution());
        var useMethod = await ResolveSymbolAsync(service, "Use", "Sample.Helper");
        var executeMethod = await ResolveSymbolAsync(service, "Execute", "Sample.Service");

        var callers = await service.GetCallersAsync(new GetCallersRequest(useMethod.SymbolId), CancellationToken.None);
        var callees = await service.GetCalleesAsync(new GetCalleesRequest(executeMethod.SymbolId), CancellationToken.None);

        callers.Error.IsNull();
        callees.Error.IsNull();
        (callers.Callers.Count >= 3).IsTrue();
        callees.Callees.Any().IsTrue();
        IsSorted(callers.Callers, CompareEdge).IsTrue();
        IsSorted(callees.Callees, CompareEdge).IsTrue();
    }

    [Fact]
    public async Task GetCallGraph_ReturnsBoundedDeduplicatedEdgesAndCounts()
    {
        var service = CreateService(CreateSampleSolution());
        var executeMethod = await ResolveSymbolAsync(service, "Execute", "Sample.Service");

        var result = await service.GetCallGraphAsync(
            new GetCallGraphRequest(executeMethod.SymbolId, CallGraphDirections.Both, MaxDepth: 10),
            CancellationToken.None);

        result.Error.IsNull();
        result.RootSymbol.IsNotNull();
        result.EdgeCount.Is(result.Edges.Count);
        (result.NodeCount >= 1).IsTrue();
        IsSorted(result.Edges, CompareEdge).IsTrue();
        result.Edges.Select(EdgeSortKey).Distinct(StringComparer.Ordinal).Count().Is(result.Edges.Count);
    }

    [Fact]
    public async Task GetCallGraph_RespectsDepthBoundWithStableOrdering()
    {
        var service = CreateService(CreateCallDepthSolution());
        var root = await ResolveSymbolAsync(service, "A", "Depth.Root");
        var terminal = await ResolveSymbolAsync(service, "F", "Depth.Root");

        var depthOne = await service.GetCallGraphAsync(
            new GetCallGraphRequest(root.SymbolId, CallGraphDirections.Outgoing, MaxDepth: 1),
            CancellationToken.None);
        var depthClamped = await service.GetCallGraphAsync(
            new GetCallGraphRequest(root.SymbolId, CallGraphDirections.Outgoing, MaxDepth: 99),
            CancellationToken.None);

        depthOne.Error.IsNull();
        depthClamped.Error.IsNull();
        depthOne.Edges.Single();
        depthClamped.Edges.Count.Is(4);
        IsSorted(depthClamped.Edges, CompareEdge).IsTrue();
        depthClamped.Edges.Any(edge => edge.ToSymbolId == terminal.SymbolId).IsFalse();
    }

    [Fact]
    public async Task FindReferences_ReturnsStructuredError_WhenSymbolIsMissing()
    {
        var service = CreateService(CreateSampleSolution());

        var result = await service.FindReferencesAsync(new FindReferencesRequest("missing-symbol"), CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.SymbolNotFound);
        result.Error?.Details.IsNotNull();
        result.Error!.Details!["symbolId"].Is("missing-symbol");
        result.Error.Details["operation"].Is("find-references");
    }

    [Fact]
    public async Task GetTypeHierarchy_RespectsTransitiveAndMaxDerivedBounds()
    {
        var service = CreateService(CreateTypeHierarchySolution());
        var baseType = await ResolveSymbolAsync(service, "BaseNode", string.Empty);

        var directOnly = await service.GetTypeHierarchyAsync(
            new GetTypeHierarchyRequest(baseType.SymbolId, IncludeTransitive: false, MaxDerived: 10),
            CancellationToken.None);
        var transitive = await service.GetTypeHierarchyAsync(
            new GetTypeHierarchyRequest(baseType.SymbolId, IncludeTransitive: true, MaxDerived: 10),
            CancellationToken.None);
        var bounded = await service.GetTypeHierarchyAsync(
            new GetTypeHierarchyRequest(baseType.SymbolId, IncludeTransitive: true, MaxDerived: 1),
            CancellationToken.None);

        directOnly.Error.IsNull();
        transitive.Error.IsNull();
        bounded.Error.IsNull();
        directOnly.DerivedTypes.Any(symbol => symbol.Name == "MidNode").IsTrue();
        directOnly.DerivedTypes.Any(symbol => symbol.Name == "LeafNode").IsFalse();
        transitive.DerivedTypes.Any(symbol => symbol.Name == "LeafNode").IsTrue();
        bounded.DerivedTypes.Single();
    }

    [Fact]
    public async Task GetSymbolOutline_ClampsDepthToMaximumAndKeepsDeterministicOrdering()
    {
        var service = CreateService(CreateOutlineDepthSolution());
        var rootType = await ResolveSymbolAsync(service, "Root", string.Empty);

        var clamped = await service.GetSymbolOutlineAsync(
            new GetSymbolOutlineRequest(rootType.SymbolId, Depth: 99),
            CancellationToken.None);
        var maxDepth = await service.GetSymbolOutlineAsync(
            new GetSymbolOutlineRequest(rootType.SymbolId, Depth: 3),
            CancellationToken.None);

        clamped.Error.IsNull();
        maxDepth.Error.IsNull();
        clamped.Members.Count.Is(maxDepth.Members.Count);
        clamped.Members.Select(MemberSortKey).Is(maxDepth.Members.Select(MemberSortKey));
        IsSorted(clamped.Members, CompareOutlineMember).IsTrue();
    }

    [Theory]
    [InlineData("find-symbol")]
    [InlineData("find-references")]
    [InlineData("find-references-scoped")]
    [InlineData("find-implementations")]
    [InlineData("get-type-hierarchy")]
    [InlineData("get-symbol-outline")]
    [InlineData("get-callers")]
    [InlineData("get-callees")]
    [InlineData("get-callgraph")]
    public async Task SymbolBasedFlows_ReturnInvalidInput_ForWhitespaceSymbolId(string operation)
    {
        var service = CreateService(CreateSampleSolution());

        var error = operation switch
        {
            "find-symbol" => (await service.FindSymbolAsync(new FindSymbolRequest("   "), CancellationToken.None)).Error,
            "find-references" => (await service.FindReferencesAsync(new FindReferencesRequest("\t"), CancellationToken.None)).Error,
            "find-references-scoped" => (await service.FindReferencesScopedAsync(new FindReferencesScopedRequest("\t", ReferenceScopes.Solution), CancellationToken.None)).Error,
            "find-implementations" => (await service.FindImplementationsAsync(new FindImplementationsRequest(" "), CancellationToken.None)).Error,
            "get-type-hierarchy" => (await service.GetTypeHierarchyAsync(new GetTypeHierarchyRequest(" "), CancellationToken.None)).Error,
            "get-symbol-outline" => (await service.GetSymbolOutlineAsync(new GetSymbolOutlineRequest("\n"), CancellationToken.None)).Error,
            "get-callers" => (await service.GetCallersAsync(new GetCallersRequest("  "), CancellationToken.None)).Error,
            "get-callees" => (await service.GetCalleesAsync(new GetCalleesRequest("\n"), CancellationToken.None)).Error,
            "get-callgraph" => (await service.GetCallGraphAsync(new GetCallGraphRequest("\r", CallGraphDirections.Incoming), CancellationToken.None)).Error,
            _ => throw new InvalidOperationException($"Unsupported operation '{operation}'.")
        };

        error.IsNotNull();
        error!.Code.Is(ErrorCodes.InvalidInput);
        Equals(error.Code, ErrorCodes.SymbolNotFound).IsFalse();
        error.Details?["parameter"].Is("symbolId");
        error.Details?["operation"].Is(operation);
    }

    [Fact]
    public async Task FindReferencesScoped_ReturnsExpectedValidationErrors()
    {
        var service = CreateService(CreateSampleSolution());
        var useMethod = await ResolveSymbolAsync(service, "Use", "Sample.Helper");

        var invalidScope = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(useMethod.SymbolId, "invalid"),
            CancellationToken.None);
        var missingPath = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(useMethod.SymbolId, ReferenceScopes.Document),
            CancellationToken.None);
        var invalidPath = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(useMethod.SymbolId, ReferenceScopes.Document, "Missing.cs"),
            CancellationToken.None);

        invalidScope.Error?.Code.Is(ErrorCodes.InvalidRequest);
        invalidScope.Error?.Details?["parameter"].Is("scope");
        missingPath.Error?.Code.Is(ErrorCodes.InvalidRequest);
        missingPath.Error?.Details?["parameter"].Is("path");
        invalidPath.Error?.Code.Is(ErrorCodes.InvalidPath);
    }

    [Fact]
    public async Task GetCallGraph_ReturnsInvalidRequest_WhenDirectionIsInvalid()
    {
        var service = CreateService(CreateSampleSolution());
        var executeMethod = await ResolveSymbolAsync(service, "Execute", "Sample.Service");

        var result = await service.GetCallGraphAsync(
            new GetCallGraphRequest(executeMethod.SymbolId, "sideways"),
            CancellationToken.None);

        result.Error?.Code.Is(ErrorCodes.InvalidRequest);
        result.Error?.Details?["parameter"].Is("direction");
        result.Error?.Details?["operation"].Is("get-callgraph");
    }

    private static INavigationService CreateService(Solution solution)
    {
        var services = new ServiceCollection();
        services.AddRoslynMcpInfrastructure();
        services.AddSingleton<IRoslynSolutionAccessor>(new TestSolutionAccessor(solution));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<INavigationService>();
    }

    private static async Task<SymbolDescriptor> ResolveSymbolAsync(INavigationService service, string name, string containingType)
    {
        var expectedContainingType = string.IsNullOrWhiteSpace(containingType) ? null : containingType;
        var symbols = await service.SearchSymbolsAsync(new SearchSymbolsRequest(name), CancellationToken.None);
        var match = symbols.Symbols.Single(s => s.Name == name && string.Equals(NormalizeDisplayName(s.ContainingType), expectedContainingType, StringComparison.Ordinal));
        return match;
    }

    private static string? NormalizeDisplayName(string? value)
        => value != null && value.StartsWith("global::", StringComparison.Ordinal) ? value[8..] : value;

    private static string SymbolSortKey(SymbolDescriptor symbol)
        => string.Join('|', symbol.Name, symbol.Kind, symbol.ContainingNamespace, symbol.ContainingType, symbol.SymbolId,
            symbol.DeclarationLocation.FilePath, symbol.DeclarationLocation.Line, symbol.DeclarationLocation.Column);

    private static string LocationSortKey(SourceLocation location)
        => string.Join('|', location.FilePath, location.Line, location.Column);

    private static string EdgeSortKey(CallEdge edge)
        => string.Join('|', edge.FromSymbolId, edge.ToSymbolId, edge.Location.FilePath, edge.Location.Line, edge.Location.Column);

    private static string MemberSortKey(SymbolMemberOutline member)
        => string.Join('|', member.Name, member.Kind, member.Signature, member.Accessibility, member.IsStatic);

    private static int CompareLocation(SourceLocation x, SourceLocation y)
    {
        var byPath = StringComparer.Ordinal.Compare(x.FilePath, y.FilePath);
        if (byPath != 0)
        {
            return byPath;
        }

        var byLine = x.Line.CompareTo(y.Line);
        return byLine != 0 ? byLine : x.Column.CompareTo(y.Column);
    }

    private static int CompareEdge(CallEdge x, CallEdge y)
    {
        var byFrom = StringComparer.Ordinal.Compare(x.FromSymbolId, y.FromSymbolId);
        if (byFrom != 0)
        {
            return byFrom;
        }

        var byTo = StringComparer.Ordinal.Compare(x.ToSymbolId, y.ToSymbolId);
        return byTo != 0 ? byTo : CompareLocation(x.Location, y.Location);
    }

    private static int CompareOutlineMember(SymbolMemberOutline x, SymbolMemberOutline y)
    {
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

    private static bool IsSorted<T>(IEnumerable<T> values, Func<T, T, int> comparer)
    {
        var hasPrevious = false;
        var previous = default(T);
        foreach (var value in values)
        {
            if (hasPrevious && comparer(previous!, value) > 0)
            {
                return false;
            }

            previous = value;
            hasPrevious = true;
        }

        return true;
    }

    private static bool IsSorted(IEnumerable<string> values)
    {
        var hasPrevious = false;
        var previous = string.Empty;
        foreach (var value in values)
        {
            if (hasPrevious && StringComparer.Ordinal.Compare(previous, value) > 0)
            {
                return false;
            }

            previous = value;
            hasPrevious = true;
        }

        return true;
    }

    private static Solution CreateSampleSolution()
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("SampleProject", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            });

        var contractsCode = """
namespace Sample;

public interface IWorker
{
    void Run();
}

public static class Helper
{
    public static void Use()
    {
    }
}
""";

        var implementationsCode = """
namespace Sample;

public sealed class WorkerA : IWorker
{
    public void Run()
    {
        Helper.Use();
    }
}

public sealed class WorkerB : IWorker
{
    public void Run()
    {
        Helper.Use();
    }
}

public sealed class Service
{
    public void Execute(IWorker worker)
    {
        worker.Run();
        Helper.Use();
    }
}
""";

        var contracts = project.AddDocument("Contracts.cs", SourceText.From(contractsCode), filePath: "Contracts.cs");
        var implementations = contracts.Project.AddDocument("Implementation.cs", SourceText.From(implementationsCode), filePath: "Implementation.cs");
        return implementations.Project.Solution;
    }

    private static Solution CreateCallDepthSolution()
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("DepthProject", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            });

        var code = """
namespace Depth;

public static class Root
{
    public static void A() => B();
    public static void B() => C();
    public static void C() => D();
    public static void D() => E();
    public static void E() => F();
    public static void F() { }
}
""";

        var document = project.AddDocument("Depth.cs", SourceText.From(code), filePath: "Depth.cs");
        return document.Project.Solution;
    }

    private static Solution CreateTypeHierarchySolution()
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("HierarchyProject", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            });

        var code = """
namespace Hierarchy;

public class BaseNode
{
}

public class MidNode : BaseNode
{
}

public class LeafNode : MidNode
{
}
""";

        var document = project.AddDocument("Hierarchy.cs", SourceText.From(code), filePath: "Hierarchy.cs");
        return document.Project.Solution;
    }

    private static Solution CreateOutlineDepthSolution()
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("OutlineProject", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            });

        var code = """
namespace Outline;

public class Root
{
    public void M() { }

    public class Level1
    {
        public class Level2
        {
            public class Level3
            {
                public class Level4
                {
                }
            }
        }
    }
}
""";

        var document = project.AddDocument("Outline.cs", SourceText.From(code), filePath: "Outline.cs");
        return document.Project.Solution;
    }

    private sealed class TestSolutionAccessor : IRoslynSolutionAccessor
    {
        private readonly Solution _solution;

        public TestSolutionAccessor(Solution solution)
        {
            _solution = solution;
        }

        public Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct)
            => Task.FromResult(((Solution?)_solution, (ErrorInfo?)null));

        public Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct)
            => Task.FromResult(((bool)true, (ErrorInfo?)null));

        public Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct)
            => Task.FromResult((1, (ErrorInfo?)null));
    }
}
