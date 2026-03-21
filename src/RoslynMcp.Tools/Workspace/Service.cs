using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcp.Tools.Workspace;

public sealed class Service : IAsyncDisposable
{
	private static readonly SemaphoreSlim Gate = new(1, 1);
	private static readonly object RegistrationLock = new();
	private static bool _msbuildRegistered;

	private Session? _current;
	private int _version;

	public async Task<(Session? Session, string SnapshotId, string WorkspaceId, string WorkspaceRoot)> LoadAsync(string solutionPath, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

		EnsureMsBuildRegistered();

		MSBuildWorkspace? workspace = null;
		try
		{
			workspace = MSBuildWorkspace.Create();
			var solution = await workspace.OpenSolutionAsync(solutionPath, progress: null, cancellationToken: cancellationToken).ConfigureAwait(false);
			var session = new Session(Extensions.WorkspaceRoot, solutionPath, workspace, solution);

			await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				var previous = _current;
				_current = session;
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

	public async Task<Session?> GetCurrentAsync(CancellationToken cancellationToken)
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
}

public sealed class Session(string workspaceRoot, string selectedSolutionPath, MSBuildWorkspace workspace, Solution solution) : IDisposable
{
	public string WorkspaceRoot { get; } = workspaceRoot;
	public string SelectedSolutionPath { get; } = selectedSolutionPath;
	public MSBuildWorkspace Workspace { get; } = workspace;
	public Solution Solution { get; } = solution;

	public void Dispose() => Workspace.Dispose();
}
