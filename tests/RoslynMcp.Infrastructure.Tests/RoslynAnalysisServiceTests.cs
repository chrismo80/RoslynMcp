using RoslynMcp.Core;
using RoslynMcp.Core.Models.Analysis;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Infrastructure.Analysis;
using RoslynMcp.Infrastructure.Workspace;
using Is.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Reflection;

namespace RoslynMcp.Infrastructure.Tests;

public sealed class RoslynAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeSolution_ReturnsDiagnostics()
    {
        var service = CreateService(CreateSolutionWithWarning());

        var result = await service.AnalyzeSolutionAsync(new AnalyzeSolutionRequest(), CancellationToken.None);

        result.Error.IsNull();
        result.Diagnostics.Any().IsTrue();
        result.Diagnostics.Any(diag => diag.Code.StartsWith("RCS", StringComparison.Ordinal)).IsTrue();
    }

    [Fact]
    public void RoslynatorAnalyzers_LoadSuccessfully()
    {
        var entry = GetRoslynatorAnalyzerEntry();

        entry.Error.IsNull();
        entry.Analyzers.IsDefaultOrEmpty.IsFalse();
    }

    [Fact]
    public async Task AnalyzeSolution_ReturnsErrorWhenSolutionMissing()
    {
        var service = CreateService(null);

        var result = await service.AnalyzeSolutionAsync(new AnalyzeSolutionRequest(), CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.SolutionNotSelected);
    }

    [Fact]
    public async Task GetCodeMetrics_ReturnsMetrics()
    {
        var service = CreateService(CreateSolutionWithComplexMethod());

        var result = await service.GetCodeMetricsAsync(new GetCodeMetricsRequest(), CancellationToken.None);

        result.Error.IsNull();
        result.Metrics.Any().IsTrue();
        result.Metrics.Any(metric => metric.CyclomaticComplexity > 1 && metric.LineCount > 0).IsTrue();
    }

    [Fact]
    public async Task GetCodeMetrics_ReturnsErrorWhenSolutionMissing()
    {
        var service = CreateService(null);

        var result = await service.GetCodeMetricsAsync(new GetCodeMetricsRequest(), CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.SolutionNotSelected);
    }

    [Fact]
    public async Task AnalyzeScope_ReturnsDiagnosticsAndMetricsForDocument()
    {
        var service = CreateService(CreateSolutionWithWarning());

        var result = await service.AnalyzeScopeAsync(new AnalyzeScopeRequest(AnalysisScopes.Document, Path.Combine("SampleProject", "Sample.cs")), CancellationToken.None);

        result.Error.IsNull();
        result.Scope.Is(AnalysisScopes.Document);
        result.Diagnostics.Any().IsTrue();
        result.Metrics.Any().IsTrue();
    }

    [Fact]
    public async Task AnalyzeScope_ReturnsPathOutOfScopeForUnknownPath()
    {
        var service = CreateService(CreateSolutionWithWarning());

        var result = await service.AnalyzeScopeAsync(new AnalyzeScopeRequest(AnalysisScopes.Document, "Missing.cs"), CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.PathOutOfScope);
    }

    private static RoslynAnalysisService CreateService(Solution? solution)
        => new(new TestSolutionAccessor(solution), NullLogger<RoslynAnalysisService>.Instance);

    private static Solution CreateSolutionWithWarning()
        => CreateSolution("Sample.cs", "namespace Sample\n{\n    public class Service\n    {\n        public void Helper(int value)\n        {\n            if (0 == value)\n            {\n                _ = value;\n            }\n        }\n    }\n}\n");

    private static Solution CreateSolutionWithComplexMethod()
        => CreateSolution("Metrics.cs", "using System;\nnamespace Sample\n{\n    public class Service\n    {\n        public void Call()\n        {\n            if (DateTime.Now.Ticks > 0)\n            {\n                Helper();\n            }\n\n            for (var i = 0; i < 3; i++)\n            {\n                Helper();\n            }\n\n            Helper();\n        }\n\n        public void Helper()\n        {\n        }\n    }\n}\n");

    private static Solution CreateSolution(string fileName, string code)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("SampleProject", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            });

        var document = project.AddDocument(fileName, SourceText.From(code), filePath: Path.Combine("SampleProject", fileName));
        return document.Project.Solution;
    }

    private sealed class TestSolutionAccessor : IRoslynSolutionAccessor
    {
        private readonly Solution? _solution;

        public TestSolutionAccessor(Solution? solution)
        {
            _solution = solution;
        }

        public Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct)
        {
            if (_solution == null)
            {
                return Task.FromResult(((Solution?)null, (ErrorInfo?)new ErrorInfo(ErrorCodes.SolutionNotSelected, "No solution selected.")));
            }

            return Task.FromResult(((Solution?)_solution, (ErrorInfo?)null));
        }

        public Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct)
            => Task.FromResult(((bool)true, (ErrorInfo?)null));

        public Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct)
            => Task.FromResult((1, (ErrorInfo?)null));
    }

    private static (ImmutableArray<DiagnosticAnalyzer> Analyzers, Exception? Error) GetRoslynatorAnalyzerEntry()
    {
        var catalogType = typeof(RoslynAnalysisService).Assembly.GetType("RoslynMcp.Infrastructure.Analysis.RoslynatorAnalyzerCatalog");
        if (catalogType == null)
        {
            throw new InvalidOperationException("Unable to locate Roslyn analyzer catalog type.");
        }

        var field = catalogType.GetField("s_roslynatorAnalyzers", BindingFlags.Static | BindingFlags.NonPublic);
        if (field == null)
        {
            throw new InvalidOperationException("Unable to locate Roslyn analyzer cache field.");
        }

        var lazy = (Lazy<(ImmutableArray<DiagnosticAnalyzer> Analyzers, Exception? Error)>)field.GetValue(null)!;
        return lazy.Value;
    }
}
