using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;

namespace RoslynMcp.Infrastructure.Workspace;

internal sealed class SolutionPathResolver : ISolutionPathResolver
{
    public (string? ResolvedPath, string? WorkspaceRoot, ErrorInfo? Error) ResolveSolutionPath(string? requestedPath, string? workspaceRootHint)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return (null, null, new ErrorInfo(ErrorCodes.InvalidPath, "Solution path must be provided."));
        }

        var candidate = requestedPath;
        if (!Path.IsPathRooted(candidate))
        {
            if (string.IsNullOrWhiteSpace(workspaceRootHint))
            {
                return (null, null, new ErrorInfo(ErrorCodes.InvalidPath,
                    "Workspace root is unknown; provide an absolute solution path or discover a workspace first."));
            }

            candidate = Path.Combine(workspaceRootHint, candidate);
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, null,
                new ErrorInfo(ErrorCodes.InvalidPath, $"Solution path '{requestedPath}' is invalid: {ex.Message}"));
        }

        if (!resolved.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null,
                new ErrorInfo(ErrorCodes.InvalidPath, $"Only .sln files can be selected ('{resolved}')."));
        }

        if (!File.Exists(resolved))
        {
            return (null, null,
                new ErrorInfo(ErrorCodes.SolutionNotFound, $"Solution '{resolved}' does not exist."));
        }

        string workspaceRoot;
        if (!string.IsNullOrWhiteSpace(workspaceRootHint) &&
            resolved.StartsWith(workspaceRootHint, StringComparison.OrdinalIgnoreCase))
        {
            workspaceRoot = workspaceRootHint;
        }
        else
        {
            var directory = Path.GetDirectoryName(resolved);
            workspaceRoot = directory ?? resolved;
        }

        return (resolved, workspaceRoot, null);
    }
}
