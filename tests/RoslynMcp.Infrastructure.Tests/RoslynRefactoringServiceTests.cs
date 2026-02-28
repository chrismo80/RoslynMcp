using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Core.Models.Refactoring;
using RoslynMcp.Infrastructure.Refactoring;
using RoslynMcp.Infrastructure.Workspace;
using Is.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace RoslynMcp.Infrastructure.Tests;

public sealed class RoslynRefactoringServiceTests
{
    [Fact]
    public async Task RenameSymbol_RenamesAcrossMultipleDocuments_AndReportsDeterministicMetadata()
    {
        var solution = CreateSampleSolution();
        var accessor = new MutableSolutionAccessor(solution);
        var service = new RoslynRefactoringService(accessor, NullLogger<RoslynRefactoringService>.Instance);
        var symbolId = await ResolveSymbolIdAsync(solution, "Helper", "Sample.Helpers");

        var result = await service.RenameSymbolAsync(new RenameSymbolRequest(symbolId, "RenamedHelper"), CancellationToken.None);

        result.Error.IsNull();
        result.ChangedDocumentCount.Is(2);
        result.ChangedFiles.Is(new[]
        {
            Path.Combine("SampleProject", "Helpers.cs"),
            Path.Combine("SampleProject", "Service.cs")
        });
        (result.AffectedLocations.Count >= 3).IsTrue();
        result.AffectedLocations.Select(l => $"{l.FilePath}:{l.Line}:{l.Column}").Distinct(StringComparer.Ordinal).Count().Is(result.AffectedLocations.Count);
    }

    [Fact]
    public async Task RenameSymbol_ReturnsRenamedSymbolId_UsableByNavigationFollowUp()
    {
        var solution = CreateSampleSolution();
        var accessor = new MutableSolutionAccessor(solution);
        var refactoring = new RoslynRefactoringService(accessor, NullLogger<RoslynRefactoringService>.Instance);
        var navigation = CreateNavigationService(accessor);
        var symbolId = await ResolveSymbolIdAsync(solution, "Helper", "Sample.Helpers");

        var rename = await refactoring.RenameSymbolAsync(new RenameSymbolRequest(symbolId, "RenamedHelper"), CancellationToken.None);
        var find = await navigation.FindSymbolAsync(new FindSymbolRequest(rename.RenamedSymbolId ?? string.Empty), CancellationToken.None);

        rename.Error.IsNull();
        rename.RenamedSymbolId.IsNotNull();
        find.Error.IsNull();
        find.Symbol?.Name.Is("RenamedHelper");
    }

    [Fact]
    public async Task RenameSymbol_ReturnsStructuredError_ForUnknownSymbol()
    {
        var service = CreateService(CreateSampleSolution());

        var result = await service.RenameSymbolAsync(new RenameSymbolRequest("missing", "Renamed"), CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.SymbolNotFound);
        result.Error?.Details?["symbolId"].Is("missing");
        result.Error?.Details?["operation"].Is("rename-symbol");
    }

    [Fact]
    public async Task RenameSymbol_ReturnsInvalidInput_ForWhitespaceSymbolId()
    {
        var service = CreateService(CreateSampleSolution());

        var result = await service.RenameSymbolAsync(new RenameSymbolRequest("   ", "Renamed"), CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.InvalidInput);
        Equals(result.Error?.Code, ErrorCodes.SymbolNotFound).IsFalse();
        result.Error?.Details?["parameter"].Is("symbolId");
        result.Error?.Details?["operation"].Is("rename-symbol");
    }

    [Fact]
    public async Task RenameSymbol_ReturnsStructuredError_ForInvalidNewName()
    {
        var solution = CreateSampleSolution();
        var service = CreateService(solution);
        var symbolId = await ResolveSymbolIdAsync(solution, "Helper", "Sample.Helpers");

        var result = await service.RenameSymbolAsync(new RenameSymbolRequest(symbolId, "123Invalid"), CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.InvalidNewName);
        result.Error?.Details?["newName"].Is("123Invalid");
        result.Error?.Details?["operation"].Is("rename-symbol");
    }

    [Fact]
    public async Task RenameSymbol_ReturnsStructuredError_ForRenameConflict()
    {
        var solution = CreateSampleSolution();
        var service = CreateService(solution);
        var symbolId = await ResolveSymbolIdAsync(solution, "Helper", "Sample.Helpers");

        var result = await service.RenameSymbolAsync(new RenameSymbolRequest(symbolId, "Existing"), CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.RenameConflict);
        result.Error?.Details?["newName"].Is("Existing");
        result.Error?.Details?["operation"].Is("rename-symbol");
    }

    [Fact]
    public async Task CodeFixFlow_ListsPreviewsAndAppliesSingleFix()
    {
        var solution = CreateSampleSolution();
        var accessor = new MutableSolutionAccessor(solution);
        var service = new RoslynRefactoringService(accessor, NullLogger<RoslynRefactoringService>.Instance);

        var fixes = await service.GetCodeFixesAsync(new GetCodeFixesRequest("document", Path.Combine("SampleProject", "Service.cs"), new[] { "CS0168" }, "compiler"), CancellationToken.None);
        var fix = fixes.Fixes.Single();

        var preview = await service.PreviewCodeFixAsync(new PreviewCodeFixRequest(fix.FixId), CancellationToken.None);
        var apply = await service.ApplyCodeFixAsync(new ApplyCodeFixRequest(fix.FixId), CancellationToken.None);

        fixes.Error.IsNull();
        preview.Error.IsNull();
        apply.Error.IsNull();
        preview.Changes.Single();
        apply.ChangedFiles.Single();
    }

    [Fact]
    public async Task CodeFixFlow_InvalidatesFixAfterWorkspaceMutation()
    {
        var solution = CreateSampleSolution();
        var accessor = new MutableSolutionAccessor(solution);
        var service = new RoslynRefactoringService(accessor, NullLogger<RoslynRefactoringService>.Instance);

        var fixes = await service.GetCodeFixesAsync(new GetCodeFixesRequest("document", Path.Combine("SampleProject", "Service.cs"), new[] { "CS0168" }, "compiler"), CancellationToken.None);
        var fix = fixes.Fixes.Single();

        var apply = await service.ApplyCodeFixAsync(new ApplyCodeFixRequest(fix.FixId), CancellationToken.None);
        var stalePreview = await service.PreviewCodeFixAsync(new PreviewCodeFixRequest(fix.FixId), CancellationToken.None);

        apply.Error.IsNull();
        stalePreview.Error.IsNotNull();
        stalePreview.Error?.Code.Is(ErrorCodes.WorkspaceChanged);
    }

    [Fact]
    public async Task GetCodeFixes_ReturnsDeterministicOrdering_ForSupportedDiagnostics()
    {
        var service = CreateService(CreateSampleSolution());

        var result = await service.GetCodeFixesAsync(new GetCodeFixesRequest("solution"), CancellationToken.None);

        result.Error.IsNull();
        (result.Fixes.Count >= 2).IsTrue();
        var ordered = result.Fixes
            .OrderBy(fix => fix.FilePath, StringComparer.Ordinal)
            .ThenBy(fix => fix.Location.Line)
            .ThenBy(fix => fix.Location.Column)
            .ThenBy(fix => fix.DiagnosticId, StringComparer.Ordinal)
            .ThenBy(fix => fix.Title, StringComparer.Ordinal)
            .ThenBy(fix => fix.FixId, StringComparer.Ordinal)
            .ToArray();
        result.Fixes.Is(ordered);
        result.Fixes.Any(fix => fix.DiagnosticId == "CS0168").IsTrue();
        result.Fixes.Any(fix => fix.DiagnosticId == "CS0219").IsTrue();
    }

    [Fact]
    public async Task RefactoringActionFlow_DiscoverPreviewApply_AndInvalidatesStaleActionId()
    {
        var solution = CreateSampleSolution();
        var accessor = new MutableSolutionAccessor(solution);
        var service = new RoslynRefactoringService(accessor, NullLogger<RoslynRefactoringService>.Instance);

        var discover = await service.GetRefactoringsAtPositionAsync(
            new GetRefactoringsAtPositionRequest(Path.Combine("SampleProject", "Service.cs"), 7, 13),
            CancellationToken.None);
        var allowAction = discover.Actions.Single(action => action.PolicyDecision.Decision == "allow");

        var preview = await service.PreviewRefactoringAsync(new PreviewRefactoringRequest(allowAction.ActionId), CancellationToken.None);
        var apply = await service.ApplyRefactoringAsync(new ApplyRefactoringRequest(allowAction.ActionId), CancellationToken.None);
        var stalePreview = await service.PreviewRefactoringAsync(new PreviewRefactoringRequest(allowAction.ActionId), CancellationToken.None);

        discover.Error.IsNull();
        preview.Error.IsNull();
        apply.Error.IsNull();
        preview.Changes.Any().IsTrue();
        stalePreview.Error.IsNotNull();
        stalePreview.Error?.Code.Is(ErrorCodes.WorkspaceChanged);
    }

    [Fact]
    public async Task RefactoringActionFlow_BlocksReviewRequiredActionOnApply()
    {
        var service = CreateService(CreateSampleSolution());

        var discover = await service.GetRefactoringsAtPositionAsync(
            new GetRefactoringsAtPositionRequest(Path.Combine("SampleProject", "Service.cs"), 7, 13),
            CancellationToken.None);
        var reviewAction = discover.Actions.Single(action => action.PolicyDecision.Decision == "review_required");

        var apply = await service.ApplyRefactoringAsync(new ApplyRefactoringRequest(reviewAction.ActionId), CancellationToken.None);

        apply.Error.IsNotNull();
        apply.Error?.Code.Is(ErrorCodes.PolicyBlocked);
    }

    [Fact]
    public async Task RefactoringActionFlow_EmitsTelemetryFields_ForSuccessAndFailure()
    {
        var solution = CreateSampleSolution();
        var accessor = new MutableSolutionAccessor(solution);
        var logger = new CapturingLogger<RoslynRefactoringService>();
        var service = new RoslynRefactoringService(accessor, logger);

        var discover = await service.GetRefactoringsAtPositionAsync(
            new GetRefactoringsAtPositionRequest(Path.Combine("SampleProject", "Service.cs"), 7, 13),
            CancellationToken.None);
        var allowAction = discover.Actions.Single(action => action.PolicyDecision.Decision == "allow");
        var apply = await service.ApplyRefactoringAsync(new ApplyRefactoringRequest(allowAction.ActionId), CancellationToken.None);
        var stalePreview = await service.PreviewRefactoringAsync(new PreviewRefactoringRequest(allowAction.ActionId), CancellationToken.None);

        apply.Error.IsNull();
        stalePreview.Error.IsNotNull();

        var discoverLog = logger.Entries.Single(entry => string.Equals(Get(entry, "Operation"), "get_refactorings_at_position", StringComparison.Ordinal)
                     && string.Equals(Get(entry, "ResultCode"), "ok", StringComparison.Ordinal));
        Get(discoverLog, "ActionOrigin").IsNotNull();
        Get(discoverLog, "ActionType").IsNotNull();
        Get(discoverLog, "PolicyDecision").IsNotNull();
        Get(discoverLog, "DurationMs").IsNotNull();
        Get(discoverLog, "AffectedDocumentCount").IsNotNull();

        var applyLog = logger.Entries.Single(entry => string.Equals(Get(entry, "Operation"), "apply_refactoring", StringComparison.Ordinal)
                     && string.Equals(Get(entry, "ResultCode"), "ok", StringComparison.Ordinal));
        Get(applyLog, "PolicyDecision").Is("allow");

        var failureLog = logger.Entries.Single(entry => string.Equals(Get(entry, "Operation"), "preview_refactoring", StringComparison.Ordinal)
                     && string.Equals(Get(entry, "ResultCode"), ErrorCodes.WorkspaceChanged, StringComparison.Ordinal));
        Get(failureLog, "ActionOrigin").Is("roslynator_codefix");
    }

    [Fact]
    public async Task RefactoringActionFlow_ReturnsProviderOrLegacyActions_WhenDiscoveringAtPosition()
    {
        var service = CreateService(CreateSampleSolution());

        var discover = await service.GetRefactoringsAtPositionAsync(
            new GetRefactoringsAtPositionRequest(Path.Combine("SampleProject", "Service.cs"), 7, 13),
            CancellationToken.None);

        discover.Error.IsNull();
        discover.Actions.Any().IsTrue();
        discover.Actions.Any(action => action.Origin == "roslynator_codefix"
                      && (DecodeProviderKey(action.ActionId).StartsWith("cf|", StringComparison.Ordinal)
                          || DecodeProviderKey(action.ActionId).StartsWith("remove_unused_local:", StringComparison.Ordinal))).IsTrue();
    }

    [Fact]
    public async Task ExecuteCleanup_AppliesSafeAutoOrderedRules_InOneCall()
    {
        var solution = CreateCleanupSampleSolution();
        var accessor = new MutableSolutionAccessor(solution);
        var service = new RoslynRefactoringService(accessor, NullLogger<RoslynRefactoringService>.Instance);

        var result = await service.ExecuteCleanupAsync(new ExecuteCleanupRequest("solution", null, "balanced", 1), CancellationToken.None);

        result.Error.IsNull();
        result.AppliedRuleIds.Is(new[]
        {
            "remove_unused_usings",
            "organize_usings",
            "fix_modifier_order",
            "add_readonly",
            "format"
        });
        result.ChangedFiles.Any().IsTrue();
        result.Warnings.Is(new[]
        {
            "meta.healthCheckPerformed=true",
            "meta.autoReloadAttempted=false",
            "meta.autoReloadSucceeded=false"
        });
    }

    [Fact]
    public async Task ExecuteCleanup_RejectsStaleExpectedWorkspaceVersion()
    {
        var solution = CreateCleanupSampleSolution();
        var accessor = new MutableSolutionAccessor(solution);
        var service = new RoslynRefactoringService(accessor, NullLogger<RoslynRefactoringService>.Instance);

        var result = await service.ExecuteCleanupAsync(new ExecuteCleanupRequest("solution", null, "balanced", 99), CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.WorkspaceChanged);
    }

    [Fact]
    public async Task ExecuteCleanup_StaleWorkspaceAfterReload_DoesNotRecreateDeletedFile()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempRoot, "CleanupTarget.cs");
            await File.WriteAllTextAsync(filePath, CreateCleanupSourceCode());
            var staleSolution = CreateCleanupSolutionForPath(filePath);
            File.Delete(filePath);

            var accessor = new ReloadingFileBackedSolutionAccessor(
                staleSolution,
                reloadSuccess: true,
                onReload: () => CreateCleanupSolutionForPath(filePath));
            var service = new RoslynRefactoringService(accessor, NullLogger<RoslynRefactoringService>.Instance);

            var result = await service.ExecuteCleanupAsync(new ExecuteCleanupRequest("solution", null, "balanced", 1), CancellationToken.None);

            result.Error.IsNotNull();
            result.Error?.Code.Is(ErrorCodes.StaleWorkspaceSnapshot);
            File.Exists(filePath).IsFalse();
            accessor.ApplyCallCount.Is(0);
            result.Error?.Details?["healthCheckPerformed"].Is("true");
            result.Error?.Details?["autoReloadAttempted"].Is("true");
            result.Error?.Details?["autoReloadSucceeded"].Is("true");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ExecuteCleanup_AutoReloadRecoversStaleWorkspace_AndContinues()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempRoot, "CleanupTarget.cs");
            await File.WriteAllTextAsync(filePath, CreateCleanupSourceCode());
            var staleSolution = CreateCleanupSolutionForPath(filePath);
            File.Delete(filePath);

            var accessor = new ReloadingFileBackedSolutionAccessor(
                staleSolution,
                reloadSuccess: true,
                onReload: () =>
                {
                    File.WriteAllText(filePath, CreateCleanupSourceCode());
                    return CreateCleanupSolutionForPath(filePath);
                });
            var service = new RoslynRefactoringService(accessor, NullLogger<RoslynRefactoringService>.Instance);

            var result = await service.ExecuteCleanupAsync(new ExecuteCleanupRequest("solution", null, "balanced", 1), CancellationToken.None);

            result.Error.IsNull();
            accessor.ReloadCallCount.Is(1);
            (accessor.ApplyCallCount > 0).IsTrue();
            result.Warnings.Is(new[]
            {
                "meta.healthCheckPerformed=true",
                "meta.autoReloadAttempted=true",
                "meta.autoReloadSucceeded=true"
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ExecuteCleanup_UnrecoverableStaleWorkspace_ReturnsStructuredErrorAndDoesNotWrite()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempRoot, "CleanupTarget.cs");
            await File.WriteAllTextAsync(filePath, CreateCleanupSourceCode());
            var staleSolution = CreateCleanupSolutionForPath(filePath);
            File.Delete(filePath);

            var accessor = new ReloadingFileBackedSolutionAccessor(
                staleSolution,
                reloadSuccess: false,
                onReload: null);
            var service = new RoslynRefactoringService(accessor, NullLogger<RoslynRefactoringService>.Instance);

            var result = await service.ExecuteCleanupAsync(new ExecuteCleanupRequest("solution", null, "balanced", 1), CancellationToken.None);

            result.Error.IsNotNull();
            result.Error?.Code.Is(ErrorCodes.StaleWorkspaceSnapshot);
            result.Error?.Message.Contains("reload_solution", StringComparison.Ordinal).IsTrue();
            result.Error?.Details?["healthCheckPerformed"].Is("true");
            result.Error?.Details?["autoReloadAttempted"].Is("true");
            result.Error?.Details?["autoReloadSucceeded"].Is("false");
            result.Error?.Details?["reloadErrorCode"].Is(ErrorCodes.InternalError);
            accessor.ApplyCallCount.Is(0);
            File.Exists(filePath).IsFalse();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static string? Get(CapturingLogger<RoslynRefactoringService>.LogEntry entry, string key)
    {
        if (entry.State.TryGetValue(key, out var value))
        {
            return value;
        }

        return entry.State.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static string DecodeProviderKey(string actionId)
    {
        var parts = actionId.Split('|');
        if (parts.Length < 6)
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(parts[5]));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static RoslynRefactoringService CreateService(Solution solution)
        => new(new ImmutableSolutionAccessor(solution), NullLogger<RoslynRefactoringService>.Instance);

    private static INavigationService CreateNavigationService(IRoslynSolutionAccessor accessor)
    {
        var services = new ServiceCollection();
        services.AddRoslynMcpInfrastructure();
        services.AddSingleton(accessor);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<INavigationService>();
    }

    private static async Task<string> ResolveSymbolIdAsync(Solution solution, string name, string containingType)
    {
        var navigation = CreateNavigationService(new ImmutableSolutionAccessor(solution));
        var searchResult = await navigation.SearchSymbolsAsync(new SearchSymbolsRequest(name), CancellationToken.None);
        var symbol = searchResult.Symbols.Single(s => s.Name == name && string.Equals(NormalizeDisplayName(s.ContainingType), containingType, StringComparison.Ordinal));
        return symbol.SymbolId;
    }

    private static string? NormalizeDisplayName(string? value)
        => value != null && value.StartsWith("global::", StringComparison.Ordinal) ? value[8..] : value;

    private static Solution CreateSampleSolution()
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("SampleProject", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            });

        var helpersCode = """
namespace Sample;

public static class Helpers
{
    public static void Existing()
    {
    }

    public static void Helper()
    {
    }
}
""";

        var serviceCode = """
namespace Sample;

public sealed class Service
{
    public void Call()
    {
        int assignedNotUsed = 0;
        int unused;
        Helpers.Helper();
        Helpers.Helper();
    }
}
""";

        var helpers = project.AddDocument("Helpers.cs", SourceText.From(helpersCode),
            filePath: Path.Combine("SampleProject", "Helpers.cs"));
        var service = helpers.Project.AddDocument("Service.cs", SourceText.From(serviceCode),
            filePath: Path.Combine("SampleProject", "Service.cs"));
        return service.Project.Solution;
    }

    private static Solution CreateCleanupSampleSolution()
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("CleanupProject", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
            });

        var code = """
using System.Text;
using System;

namespace Sample;

public sealed class CleanupTarget
{
    static private int _counter;

    public CleanupTarget()
    {
        _counter = 1;
    }

    public void Print()
    {
        Console.WriteLine(_counter);
    }
}
""";

        var document = project.AddDocument("CleanupTarget.cs", SourceText.From(code),
            filePath: Path.Combine("CleanupProject", "CleanupTarget.cs"));
        return document.Project.Solution;
    }

    private static Solution CreateCleanupSolutionForPath(string filePath)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("CleanupProject", LanguageNames.CSharp)
            .WithMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
            });

        var document = project.AddDocument("CleanupTarget.cs", SourceText.From(CreateCleanupSourceCode()), filePath: filePath);
        return document.Project.Solution;
    }

    private static string CreateCleanupSourceCode()
        => """
using System.Text;
using System;

namespace Sample;

public sealed class CleanupTarget
{
    static private int _counter;

    public CleanupTarget()
    {
        _counter = 1;
    }

    public void Print()
    {
        Console.WriteLine(_counter);
    }
}
""";

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RoslynMcpRefactoringTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => true;

        public void Log<TState>(LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> kvp)
            {
                foreach (var pair in kvp)
                {
                    if (pair.Value != null)
                    {
                        map[pair.Key] = pair.Value.ToString() ?? string.Empty;
                    }
                }
            }

            Entries.Add(new LogEntry(logLevel, map));
        }

        public sealed record LogEntry(LogLevel Level, IReadOnlyDictionary<string, string> State);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class ImmutableSolutionAccessor : IRoslynSolutionAccessor
    {
        private readonly Solution _solution;

        public ImmutableSolutionAccessor(Solution solution)
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

    private sealed class MutableSolutionAccessor : IRoslynSolutionAccessor
    {
        private Solution _solution;
        private int _version = 1;

        public MutableSolutionAccessor(Solution solution)
        {
            _solution = solution;
        }

        public Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct)
            => Task.FromResult(((Solution?)_solution, (ErrorInfo?)null));

        public Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct)
        {
            _solution = solution;
            _version++;
            return Task.FromResult(((bool)true, (ErrorInfo?)null));
        }

        public Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct)
            => Task.FromResult((_version, (ErrorInfo?)null));
    }

    private sealed class ReloadingFileBackedSolutionAccessor : IRoslynSolutionAccessor, ISolutionSessionService
    {
        private Solution _solution;
        private readonly bool _reloadSuccess;
        private readonly Func<Solution>? _onReload;
        private int _version = 1;

        public ReloadingFileBackedSolutionAccessor(Solution initialSolution, bool reloadSuccess, Func<Solution>? onReload)
        {
            _solution = initialSolution;
            _reloadSuccess = reloadSuccess;
            _onReload = onReload;
        }

        public int ReloadCallCount { get; private set; }

        public int ApplyCallCount { get; private set; }

        public Task<DiscoverSolutionsResult> DiscoverSolutionsAsync(DiscoverSolutionsRequest request, CancellationToken ct)
            => Task.FromResult(new DiscoverSolutionsResult(Array.Empty<string>(), new ErrorInfo(ErrorCodes.InternalError, "Not implemented in test accessor.")));

        public Task<SelectSolutionResult> SelectSolutionAsync(SelectSolutionRequest request, CancellationToken ct)
            => Task.FromResult(new SelectSolutionResult(null, new ErrorInfo(ErrorCodes.InternalError, "Not implemented in test accessor.")));

        public Task<ReloadSolutionResult> ReloadSolutionAsync(ReloadSolutionRequest request, CancellationToken ct)
        {
            ReloadCallCount++;
            if (!_reloadSuccess)
            {
                return Task.FromResult(new ReloadSolutionResult(false, new ErrorInfo(ErrorCodes.InternalError, "Reload failed in test accessor.")));
            }

            if (_onReload != null)
            {
                _solution = _onReload();
            }

            _version++;
            return Task.FromResult(new ReloadSolutionResult(true));
        }

        public Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct)
            => Task.FromResult(((Solution?)_solution, (ErrorInfo?)null));

        public async Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct)
        {
            ApplyCallCount++;
            _solution = solution;
            _version++;

            foreach (var document in solution.Projects.SelectMany(static project => project.Documents))
            {
                var filePath = document.FilePath;
                if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
                {
                    continue;
                }

                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(filePath, text.ToString(), ct).ConfigureAwait(false);
            }

            return (true, null);
        }

        public Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct)
            => Task.FromResult((_version, (ErrorInfo?)null));
    }
}
