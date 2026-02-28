using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Refactoring;
using RoslynMcp.Infrastructure.Agent;
using RoslynMcp.Infrastructure.Workspace;
using Is.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Infrastructure.Tests;

public sealed class CodeSmellFindingServiceTests
{
    [Fact]
    public async Task FindCodeSmells_ReturnsFlatActions()
    {
        var solution = CreateSolution();
        var refactoring = new RecordingRefactoringService(
            new Dictionary<(string FileName, int Line), GetRefactoringsAtPositionResult>(StringTupleComparer.OrdinalIgnoreCase)
            {
                [("A.cs", 8)] = new GetRefactoringsAtPositionResult(
                    new[]
                    {
                        CreateAction("a-2", "Extract method", "refactor", "review_required"),
                        CreateAction("a-1", "Use expression body", "style", "safe")
                    }),
                [("A.cs", 7)] = new GetRefactoringsAtPositionResult(new[] { CreateAction("a-3", "Inline property", "refactor", "safe") })
            });

        var service = new CodeSmellFindingService(new RecordingSolutionAccessor(solution), refactoring);
        var result = await service.FindCodeSmellsAsync(
            new FindCodeSmellsRequest(Path.Combine("SampleProject", "A.cs")),
            CancellationToken.None);

        result.Error.IsNull();
        result.Actions.Any().IsTrue();
        (result.Actions.Count >= 3).IsTrue();
        result.Actions.Any(action => action.Title == "Use expression body").IsTrue();
        result.Actions.All(action =>
                !string.IsNullOrWhiteSpace(action.Title)
                && !string.IsNullOrWhiteSpace(action.Category)
                && !string.IsNullOrWhiteSpace(action.Location.FilePath)
                && !string.IsNullOrWhiteSpace(action.Origin)
                && !string.IsNullOrWhiteSpace(action.RiskLevel))
            .IsTrue();
        result.Warnings.Count.Is(0);
    }

    [Fact]
    public async Task FindCodeSmells_ValidatesPath()
    {
        var solution = CreateSolution();
        var service = new CodeSmellFindingService(
            new RecordingSolutionAccessor(solution),
            new RecordingRefactoringService(new Dictionary<(string FileName, int Line), GetRefactoringsAtPositionResult>(StringTupleComparer.OrdinalIgnoreCase)));

        var missingPath = await service.FindCodeSmellsAsync(
            new FindCodeSmellsRequest("  "),
            CancellationToken.None);
        var invalidPath = await service.FindCodeSmellsAsync(
            new FindCodeSmellsRequest("Missing.cs"),
            CancellationToken.None);

        missingPath.Error?.Code.Is(ErrorCodes.InvalidInput);
        invalidPath.Error?.Code.Is(ErrorCodes.InvalidPath);
    }

    [Fact]
    public async Task FindCodeSmells_PathMustResolveToExactlyOneDocument()
    {
        var duplicatePath = Path.Combine("Shared", "Same.cs");
        var solution = CreateSolutionWithDuplicatePath(duplicatePath);
        var service = new CodeSmellFindingService(
            new RecordingSolutionAccessor(solution),
            new RecordingRefactoringService(new Dictionary<(string FileName, int Line), GetRefactoringsAtPositionResult>(StringTupleComparer.OrdinalIgnoreCase)));

        var result = await service.FindCodeSmellsAsync(
            new FindCodeSmellsRequest(duplicatePath),
            CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.InvalidPath);
    }

    [Fact]
    public async Task FindCodeSmells_UsesDiagnosticAnchors()
    {
        var filePath = Path.Combine("SampleProject", "DiagnosticSample.cs");
        var solution = CreateSolution(filePath, """
namespace Sample
{
    public class DiagnosticSample
    {
        public void Run(int input)
        {
            int unused;
            _ = input;
        }
    }
}
""");

        var refactoring = new RecordingRefactoringService(
            new Dictionary<(string FileName, int Line), GetRefactoringsAtPositionResult>(StringTupleComparer.OrdinalIgnoreCase)
            {
                [("DiagnosticSample.cs", 7)] = new GetRefactoringsAtPositionResult(
                    new[]
                    {
                        CreateAction("diag-1", "Remove unused variable", "compiler", "safe", diagnosticId: "CS0168")
                    })
            });

        var service = new CodeSmellFindingService(new RecordingSolutionAccessor(solution), refactoring);
        var result = await service.FindCodeSmellsAsync(
            new FindCodeSmellsRequest(filePath),
            CancellationToken.None);

        result.Error.IsNull();
        result.Actions.Any().IsTrue();
        result.Actions.Any(action =>
                Path.GetFileName(action.Location.FilePath) == "DiagnosticSample.cs"
                && action.Location.Line == 7
                && action.Category == "compiler")
            .IsTrue();
    }

    private static RefactoringActionDescriptor CreateAction(string id, string title, string category, string risk, string? diagnosticId = null)
        => new(
            id,
            title,
            category,
            "roslyn_refactoring",
            risk,
            new PolicyDecisionInfo(risk == "safe" ? "allow" : "review_required", "rule", "reason"),
            new SourceLocation("A.cs", 1, 1),
            diagnosticId);

    private static Solution CreateSolution()
    {
        return CreateSolution(
            Path.Combine("SampleProject", "A.cs"),
            """
namespace Sample
{
    public class Alpha
    {
        private int _value;
        public Alpha() { }
        public int Value { get; set; }
        public void Run() { }
    }
}
""",
            Path.Combine("SampleProject", "B.cs"),
            """
namespace Sample
{
    public class Beta
    {
        public void Call() { }
    }
}
""");
    }

    private static Solution CreateSolution(string firstFilePath, string firstCode)
        => CreateSolution(
            firstFilePath,
            firstCode,
            Path.Combine("SampleProject", "Secondary.cs"),
            """
namespace Sample
{
    public class Secondary
    {
    }
}
""");

    private static Solution CreateSolution(string firstFilePath, string firstCode, string secondFilePath, string secondCode)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("SampleProject", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview));

        var firstDocument = project.AddDocument(Path.GetFileName(firstFilePath), SourceText.From(firstCode), filePath: firstFilePath);
        var b = firstDocument.Project.AddDocument(Path.GetFileName(secondFilePath), SourceText.From(secondCode), filePath: secondFilePath);
        return b.Project.Solution;
    }

    private static Solution CreateSolutionWithDuplicatePath(string filePath)
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var projectAId = ProjectId.CreateNewId("ProjectA");
        var projectBId = ProjectId.CreateNewId("ProjectB");

        solution = solution.AddProject(ProjectInfo.Create(
            projectAId,
            VersionStamp.Create(),
            "ProjectA",
            "ProjectA",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview)));

        solution = solution.AddProject(ProjectInfo.Create(
            projectBId,
            VersionStamp.Create(),
            "ProjectB",
            "ProjectB",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview)));

        solution = solution.AddDocument(DocumentId.CreateNewId(projectAId), "Same.cs", SourceText.From("public class A { }"), filePath: filePath);
        solution = solution.AddDocument(DocumentId.CreateNewId(projectBId), "Same.cs", SourceText.From("public class B { }"), filePath: filePath);

        return solution;
    }

    private sealed class RecordingSolutionAccessor : IRoslynSolutionAccessor
    {
        private readonly Solution _solution;

        public RecordingSolutionAccessor(Solution solution)
        {
            _solution = solution;
        }

        public Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct)
            => Task.FromResult<(Solution? Solution, ErrorInfo? Error)>((_solution, null));

        public Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct)
            => Task.FromResult((0, (ErrorInfo?)null));

        public Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct)
            => Task.FromResult((true, (ErrorInfo?)null));
    }

    private sealed class RecordingRefactoringService : IRefactoringService
    {
        private readonly IReadOnlyDictionary<(string FileName, int Line), GetRefactoringsAtPositionResult> _responses;

        public RecordingRefactoringService(IReadOnlyDictionary<(string FileName, int Line), GetRefactoringsAtPositionResult> responses)
        {
            _responses = responses;
        }

        public Task<GetRefactoringsAtPositionResult> GetRefactoringsAtPositionAsync(GetRefactoringsAtPositionRequest request, CancellationToken ct)
            => Task.FromResult(_responses.TryGetValue((Path.GetFileName(request.Path), request.Line), out var result)
                ? result
                : new GetRefactoringsAtPositionResult(Array.Empty<RefactoringActionDescriptor>()));

        public Task<PreviewRefactoringResult> PreviewRefactoringAsync(PreviewRefactoringRequest request, CancellationToken ct)
            => Task.FromResult(new PreviewRefactoringResult(request.ActionId, string.Empty, Array.Empty<ChangedFilePreview>()));

        public Task<ApplyRefactoringResult> ApplyRefactoringAsync(ApplyRefactoringRequest request, CancellationToken ct)
            => Task.FromResult(new ApplyRefactoringResult(request.ActionId, 0, Array.Empty<string>()));

        public Task<GetCodeFixesResult> GetCodeFixesAsync(GetCodeFixesRequest request, CancellationToken ct)
            => Task.FromResult(new GetCodeFixesResult(Array.Empty<CodeFixDescriptor>()));

        public Task<PreviewCodeFixResult> PreviewCodeFixAsync(PreviewCodeFixRequest request, CancellationToken ct)
            => Task.FromResult(new PreviewCodeFixResult(request.FixId, string.Empty, Array.Empty<ChangedFilePreview>()));

        public Task<ApplyCodeFixResult> ApplyCodeFixAsync(ApplyCodeFixRequest request, CancellationToken ct)
            => Task.FromResult(new ApplyCodeFixResult(request.FixId, 0, Array.Empty<string>()));

        public Task<ExecuteCleanupResult> ExecuteCleanupAsync(ExecuteCleanupRequest request, CancellationToken ct)
            => Task.FromResult(new ExecuteCleanupResult(request.Scope, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

        public Task<RenameSymbolResult> RenameSymbolAsync(RenameSymbolRequest request, CancellationToken ct)
            => Task.FromResult(new RenameSymbolResult(null, 0, Array.Empty<SourceLocation>(), Array.Empty<string>()));
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string FileName, int Line)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();

        public bool Equals((string FileName, int Line) x, (string FileName, int Line) y)
            => x.Line == y.Line && string.Equals(x.FileName, y.FileName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string FileName, int Line) obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FileName), obj.Line);
    }
}
