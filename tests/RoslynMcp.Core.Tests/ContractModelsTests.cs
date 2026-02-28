using RoslynMcp.Core;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Analysis;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Core.Models.Refactoring;
using Is.Assertions;

namespace RoslynMcp.Core.Tests;

public sealed class ContractModelsTests
{
    [Fact]
    public void ErrorInfo_PreservesStructuredDetails()
    {
        var details = new Dictionary<string, string>
        {
            ["symbolId"] = "abc123",
            ["operation"] = "find-symbol"
        };

        var error = new ErrorInfo(ErrorCodes.SymbolNotFound, "Symbol was not found.", details);

        error.Code.Is(ErrorCodes.SymbolNotFound);
        error.Message.Is("Symbol was not found.");
        error.Details.IsNotNull();
        error.Details!["symbolId"].Is("abc123");
        error.Details["operation"].Is("find-symbol");
    }

    [Fact]
    public void NavigationResults_ExposeStablePayloadShapes()
    {
        var location = new SourceLocation("Sample.cs", 10, 5);
        var symbol = new SymbolDescriptor("symbol-key", "Helper", "Method", "Sample.Service", "Sample", location);
        var error = new ErrorInfo(ErrorCodes.InternalError, "Unexpected failure.");
        var member = new SymbolMemberOutline("Run", "Method", "void Run()", "Public", false);
        var edge = new CallEdge("from", "to", location);

        var findResult = new FindSymbolResult(symbol, error);
        var atPositionResult = new GetSymbolAtPositionResult(symbol, error);
        var referencesResult = new FindReferencesResult(symbol, new[] { location }, error);
        var scopedSearchResult = new SearchSymbolsScopedResult(new[] { symbol }, 1, error);
        var signatureResult = new GetSignatureResult(symbol, "void Helper()", error);
        var scopedReferencesResult = new FindReferencesScopedResult(symbol, new[] { location }, 1, error);
        var hierarchyResult = new GetTypeHierarchyResult(symbol, new[] { symbol }, new[] { symbol }, new[] { symbol }, error);
        var outlineResult = new GetSymbolOutlineResult(symbol, new[] { member }, new[] { "System.ObsoleteAttribute" }, error);
        var graphResult = new GetCallGraphResult(symbol, new[] { edge }, 2, 1, error);

        ReferenceEquals(symbol, findResult.Symbol).IsTrue();
        ReferenceEquals(error, findResult.Error).IsTrue();
        referencesResult.References.Single();
        ReferenceEquals(error, atPositionResult.Error).IsTrue();
        scopedSearchResult.TotalCount.Is(1);
        signatureResult.Signature.Is("void Helper()");
        scopedReferencesResult.TotalCount.Is(1);
        hierarchyResult.BaseTypes.Single();
        outlineResult.Members.Single();
        graphResult.Edges.Single();
        graphResult.NodeCount.Is(2);
        ReferenceEquals(error, referencesResult.Error).IsTrue();
    }

    [Fact]
    public void RenameSymbolResult_ContainsImpactMetadataAndError()
    {
        var affected = new[]
        {
            new SourceLocation("A.cs", 3, 4),
            new SourceLocation("B.cs", 8, 2)
        };

        var changedFiles = new[] { "A.cs", "B.cs" };
        var error = new ErrorInfo(ErrorCodes.RenameConflict, "Rename would conflict.");
        var result = new RenameSymbolResult("renamed-id", 2, affected, changedFiles, error);

        result.RenamedSymbolId.Is("renamed-id");
        result.ChangedDocumentCount.Is(2);
        result.AffectedLocations.Count.Is(2);
        result.ChangedFiles.Count.Is(2);
        ReferenceEquals(error, result.Error).IsTrue();
    }

    [Fact]
    public void CodeFixContracts_ExposeDeterministicPayloadShape()
    {
        var location = new SourceLocation("A.cs", 10, 4);
        var fix = new CodeFixDescriptor("fix-id", "Remove unused", "CS0168", "compiler", location, "A.cs");
        var error = new ErrorInfo(ErrorCodes.FixConflict, "Conflict");
        var fixesResult = new GetCodeFixesResult(new[] { fix }, error);
        var preview = new PreviewCodeFixResult("fix-id", "Remove unused", new[] { new ChangedFilePreview("A.cs", 1) }, error);
        var apply = new ApplyCodeFixResult("fix-id", 1, new[] { "A.cs" }, error);
        var scoped = new AnalyzeScopeResult("document", "A.cs", Array.Empty<DiagnosticItem>(), Array.Empty<MetricItem>(), error);

        fixesResult.Fixes.Single();
        preview.FixId.Is("fix-id");
        apply.ChangedDocumentCount.Is(1);
        scoped.Scope.Is("document");
        ReferenceEquals(error, apply.Error).IsTrue();
    }

    [Fact]
    public void RefactoringActionContracts_ExposePolicyAndMutationPayloadShape()
    {
        var location = new SourceLocation("A.cs", 7, 3);
        var policy = new PolicyDecisionInfo("allow", "allowlisted", "Action is allowlisted.");
        var descriptor = new RefactoringActionDescriptor(
            "action-id",
            "Remove unused local variable 'unused'",
            "compiler",
            "roslynator_codefix",
            "safe",
            policy,
            location,
            "CS0168",
            null);

        var discover = new GetRefactoringsAtPositionResult(new[] { descriptor });
        var preview = new PreviewRefactoringResult("action-id", descriptor.Title, new[] { new ChangedFilePreview("A.cs", 1) });
        var apply = new ApplyRefactoringResult("action-id", 1, new[] { "A.cs" });

        discover.Actions.Single();
        descriptor.PolicyDecision.Decision.Is("allow");
        descriptor.RiskLevel.Is("safe");
        preview.ActionId.Is("action-id");
        apply.ChangedDocumentCount.Is(1);
    }

    [Fact]
    public void AgentDiscoveryContracts_ExposeTypeMemberResolveShapes()
    {
        var hotspot = new HotspotSummary(
            Label: "Service.Call()",
            Path: "Sample.cs",
            Reason: "complexity=2, lines=5",
            Score: 7,
            SymbolId: "sym-id",
            DisplayName: "Service.Call()",
            FilePath: "Sample.cs",
            StartLine: 10,
            EndLine: 14,
            Complexity: 2,
            LineCount: 5);

        var type = new TypeListEntry("Service", "type-id", "Sample.cs", 3, 18, "class", false, null);
        var member = new MemberListEntry("Call", "member-id", "method", "void Service.Call()", "Sample.cs", 10, 17, "public", false);
        var resolved = new ResolvedSymbolSummary("member-id", "Service.Call()", "Method", "Sample.cs", 10, 17);
        var candidate = new ResolveSymbolCandidate("member-id", "Service.Call()", "Method", "Sample.cs", 10, 17, "SampleProject");

        var listTypes = new ListTypesResult(new[] { type }, 1);
        var listMembers = new ListMembersResult(new[] { member }, 1, IncludeInherited: false);
        var resolve = new ResolveSymbolResult(resolved, IsAmbiguous: false, new[] { candidate });
        var discoverRequest = new FindCodeSmellsRequest("Sample.cs");
        var discoverResult = new FindCodeSmellsResult(
            new[]
            {
                new CodeSmellMatch(
                    "Extract method",
                    "refactor",
                    new SourceLocation("Sample.cs", 10, 17),
                    "provider",
                    "safe")
            },
            Array.Empty<string>());

        hotspot.SymbolId.Is("sym-id");
        hotspot.DisplayName.Is("Service.Call()");
        hotspot.FilePath.Is("Sample.cs");
        listTypes.TotalCount.Is(1);
        listMembers.Members.Single().Signature.Contains("Call", StringComparison.Ordinal).IsTrue();
        resolve.Symbol?.SymbolId.Is("member-id");
        resolve.Candidates.Single().ProjectName.Is("SampleProject");
        discoverRequest.Path.Is("Sample.cs");
        discoverResult.Actions.Single().Category.Is("refactor");
    }
}