using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Infrastructure.Workspace;

internal sealed class WorkspaceRootDiscovery : IWorkspaceRootDiscovery
{
    private readonly ILogger _logger;

    public WorkspaceRootDiscovery(ILogger<WorkspaceRootDiscovery>? logger = null)
    {
        _logger = logger ?? NullLogger<WorkspaceRootDiscovery>.Instance;
    }

    public (string? NormalizedRoot, ErrorInfo? Error) NormalizeWorkspaceRoot(string? workspaceRoot)
    {
        var root = workspaceRoot?.Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            return (null, new ErrorInfo(ErrorCodes.InvalidPath, "Workspace root must be provided."));
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, new ErrorInfo(ErrorCodes.InvalidPath, $"Workspace root '{root}' is invalid: {ex.Message}"));
        }

        if (!Directory.Exists(normalizedRoot))
        {
            return (null, new ErrorInfo(ErrorCodes.InvalidPath, $"Workspace root '{normalizedRoot}' could not be found."));
        }

        return (normalizedRoot, null);
    }

    public Task<(IReadOnlyList<string> Solutions, ErrorInfo? Error)> DiscoverSolutionsAsync(string normalizedRoot, CancellationToken ct)
    {
        var solutions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var pattern in new[] { "*.sln", "*.slnx" })
            {
                foreach (var solutionPath in Directory.EnumerateFiles(normalizedRoot, pattern, SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    solutions.Add(Path.GetFullPath(solutionPath));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate solutions under {WorkspaceRoot}", normalizedRoot);
            return Task.FromResult(((IReadOnlyList<string>)Array.Empty<string>(),
                (ErrorInfo?)new ErrorInfo(ErrorCodes.InvalidPath, $"Failed to read workspace '{normalizedRoot}': {ex.Message}")));
        }

        var orderedSolutions = solutions.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.FromResult(((IReadOnlyList<string>)orderedSolutions, (ErrorInfo?)null));
    }
}
