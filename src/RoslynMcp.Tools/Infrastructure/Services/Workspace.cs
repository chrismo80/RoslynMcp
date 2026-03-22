using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcp.Tools.Infrastructure.Services;

public class Workspace : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Lock RegistrationLock = new();
    private static bool _msbuildRegistered;

    private Session? _current;
    private int _version;
    private readonly Dictionary<string, string> _symbolAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _canonicalSymbolIds = new(StringComparer.Ordinal);

    internal async Task<(Session? Session, string SnapshotId, string WorkspaceId, string WorkspaceRoot)> LoadAsync(string solutionPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

        EnsureMsBuildRegistered();

        MSBuildWorkspace? workspace = null;
        try
        {
            workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath, progress: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            var session = new Session(Path.GetFullPath(Directory.GetCurrentDirectory()), solutionPath, workspace, solution);

            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var previous = _current;
                _current = session;
                _symbolAliases.Clear();
                _canonicalSymbolIds.Clear();
                _version++;
                previous?.Dispose();

                var snapshotId = _version.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return (_current, snapshotId, _current.SelectedSolutionPath, _current.WorkspaceRoot);
            }
            finally
            {
                Gate.Release();
            }
        }
        catch
        {
            workspace?.Dispose();
            throw;
        }
    }

    internal async Task<Session?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _current;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _current?.Dispose();
            _current = null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void EnsureMsBuildRegistered()
    {
        if (_msbuildRegistered)
            return;

        lock (RegistrationLock)
        {
            if (_msbuildRegistered)
                return;

            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();

            _msbuildRegistered = true;
        }
    }

    internal async Task<bool> ApplyChangesAsync(Solution solution, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current is null)
                return false;

            if (!TryApplyChanges(_current, solution))
                return false;

            _current.UpdateSolution(_current.Workspace.CurrentSolution);
            _version++;
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    internal async Task<bool> ReloadAsync(CancellationToken cancellationToken)
    {
        Session? current;
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = _current;
            if (current is null)
                return false;
        }
        finally
        {
            Gate.Release();
        }

        MSBuildWorkspace? reloadedWorkspace = null;
        try
        {
            EnsureMsBuildRegistered();
            reloadedWorkspace = MSBuildWorkspace.Create();
            var reloadedSolution = await reloadedWorkspace.OpenSolutionAsync(current.SelectedSolutionPath, progress: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            var replacement = new Session(current.WorkspaceRoot, current.SelectedSolutionPath, reloadedWorkspace, reloadedSolution);

            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var previous = _current;
                _current = replacement;
                _symbolAliases.Clear();
                _canonicalSymbolIds.Clear();
                _version++;
                previous?.Dispose();
                reloadedWorkspace = null;
                return true;
            }
            finally
            {
                Gate.Release();
            }
        }
        catch
        {
            reloadedWorkspace?.Dispose();
            throw;
        }
    }

    internal virtual bool TryApplyChanges(Session session, Solution solution)
        => session.Workspace.TryApplyChanges(solution);

    internal async Task<string?> ResolveAliasedSymbolIdAsync(string symbolId, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _symbolAliases.TryGetValue(symbolId, out var mapped) ? mapped : null;
        }
        finally
        {
            Gate.Release();
        }
    }

    internal async Task SetAliasedSymbolIdAsync(string originalSymbolId, string currentSymbolId, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _symbolAliases[originalSymbolId] = currentSymbolId;
            _canonicalSymbolIds[currentSymbolId] = originalSymbolId;
        }
        finally
        {
            Gate.Release();
        }
    }

    internal async Task<string> GetCanonicalSymbolIdAsync(string symbolId, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _canonicalSymbolIds.TryGetValue(symbolId, out var canonical) ? canonical : symbolId;
        }
        finally
        {
            Gate.Release();
        }
    }
}
