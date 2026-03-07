using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Infrastructure;

namespace RoslynMcp.Features.Tests;

public abstract class SandboxContext : IAsyncDisposable
{
    private ServiceProvider? _provider;

    protected SandboxContext()
    {
        RepositoryRoot = GetRepositoryRoot();
        CanonicalTestSolutionDirectory = Path.GetFullPath(Path.Combine(RepositoryRoot, "tests", "TestSolution"));
        CanonicalSolutionPath = Path.Combine(CanonicalTestSolutionDirectory, "TestSolution.sln");
    }

    public string RepositoryRoot { get; }

    public string CanonicalTestSolutionDirectory { get; }

    public string CanonicalSolutionPath { get; }

    public string SolutionPath => Sandbox?.SolutionPath ?? throw new InvalidOperationException("The feature test sandbox has not been initialized.");

    public string TestSolutionDirectory => Sandbox?.SolutionRoot ?? throw new InvalidOperationException("The feature test sandbox has not been initialized.");

    public LoadSolutionResult LoadedSolution { get; private set; } = default!;

    protected TestSolutionSandbox? Sandbox { get; private set; }

    public T GetRequiredService<T>() where T : notnull
        => (_provider ?? throw new InvalidOperationException("The feature test services have not been initialized."))
            .GetRequiredService<T>();

    public ProjectSummary GetProject(string projectName)
        => LoadedSolution.Projects.Single(project => project.Name == projectName);

    public string GetFilePath(string project, string file)
        => Path.Combine(TestSolutionDirectory, project, $"{file}.cs");

    protected async Task InitializeSandboxAsync(TestSolutionSandbox sandbox, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sandbox);

        if (_provider is not null || Sandbox is not null)
        {
            throw new InvalidOperationException("The sandbox context has already been initialized.");
        }

        ServiceProvider? provider = null;
        try
        {
            Sandbox = sandbox;
            provider = CreateServiceProvider();
            _provider = provider;

            LoadedSolution = await GetRequiredService<LoadSolutionTool>()
                .ExecuteAsync(cancellationToken, SolutionPath)
                .ConfigureAwait(false);

            if (LoadedSolution.Error is not null)
            {
                throw new InvalidOperationException($"Failed to load sandbox solution '{SolutionPath}': {LoadedSolution.Error.Message}");
            }
        }
        catch
        {
            if (provider is not null)
            {
                await provider.DisposeAsync().ConfigureAwait(false);
                _provider = null;
            }

            Sandbox?.Dispose();
            Sandbox = null;
            LoadedSolution = default!;
            throw;
        }
    }

    protected virtual ServiceProvider CreateServiceProvider()
        => new ServiceCollection()
            .AddInfrastructure()
            .AddImplementations<Tool>()
            .BuildServiceProvider();

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
            _provider = null;
        }

        Sandbox?.Dispose();
        Sandbox = null;
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var markerPath = Path.Combine(current.FullName, "RoslynMcp.slnx");
            if (File.Exists(markerPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from AppContext.BaseDirectory.");
    }
}
