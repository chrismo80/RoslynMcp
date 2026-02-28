using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;

namespace RoslynMcp.Infrastructure.Analysis;

internal sealed class RoslynatorAnalyzerCatalog : IRoslynAnalyzerCatalog
{
    private const string RoslynatorAnalyzerPackageId = "roslynator.analyzers";
    private const string RoslynatorAnalyzerPackageVersion = "4.15.0";
    private const string RoslynatorAnalyzerFilename = "Roslynator.CSharp.Analyzers.dll";
    private const string RoslynatorAnalyzerPathEnvVar = "RoslynMcp__RoslynatorAnalyzerPath";

    private static readonly string[] RoslynatorAnalyzerRelativePathSegments =
        { "analyzers", "dotnet", "roslyn4.7", "cs", RoslynatorAnalyzerFilename };

    private static readonly Lazy<(ImmutableArray<DiagnosticAnalyzer> Analyzers, Exception? Error)> s_roslynatorAnalyzers
        = new(LoadAnalyzers);

    public (ImmutableArray<DiagnosticAnalyzer> Analyzers, Exception? Error) GetCatalog()
        => s_roslynatorAnalyzers.Value;

    private static (ImmutableArray<DiagnosticAnalyzer> Analyzers, Exception? Error) LoadAnalyzers()
    {
        try
        {
            var analyzerPath = ResolveRoslynatorAnalyzerAssemblyPath();
            var loader = new RoslynatorAnalyzerLoader();
            loader.AddDependencyLocation(analyzerPath);
            var reference = new AnalyzerFileReference(analyzerPath, loader);
            var analyzers = reference.GetAnalyzers(LanguageNames.CSharp);
            return (analyzers, null);
        }
        catch (Exception ex)
        {
            return (ImmutableArray<DiagnosticAnalyzer>.Empty, ex);
        }
    }

    private static string ResolveRoslynatorAnalyzerAssemblyPath()
    {
        var overridePath = Environment.GetEnvironmentVariable(RoslynatorAnalyzerPathEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (File.Exists(overridePath))
            {
                return overridePath;
            }

            throw new InvalidOperationException(
                $"Roslynator analyzer path '{overridePath}' configured via '{RoslynatorAnalyzerPathEnvVar}' could not be found.");
        }

        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        var packageDirectory = Path.Combine(packagesRoot, RoslynatorAnalyzerPackageId, RoslynatorAnalyzerPackageVersion);
        if (!Directory.Exists(packageDirectory))
        {
            throw new InvalidOperationException(
                $"NuGet package '{RoslynatorAnalyzerPackageId}' version '{RoslynatorAnalyzerPackageVersion}' was not found under '{packagesRoot}'.");
        }

        var candidate = Path.Combine(packageDirectory, Path.Combine(RoslynatorAnalyzerRelativePathSegments));
        if (!File.Exists(candidate))
        {
            throw new InvalidOperationException(
                $"Unable to locate '{RoslynatorAnalyzerFilename}' under '{packageDirectory}'. Ensure the '{RoslynatorAnalyzerPackageId}' package version '{RoslynatorAnalyzerPackageVersion}' is restored.");
        }

        return candidate;
    }

    private sealed class RoslynatorAnalyzerLoader : IAnalyzerAssemblyLoader
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Assembly> _loadedAssemblies = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public RoslynatorAnalyzerLoader()
        {
            AssemblyLoadContext.Default.Resolving += OnResolving;
        }

        public void AddDependencyLocation(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                lock (_gate)
                {
                    _directories.Add(directory);
                }
            }

            LoadFromPath(fullPath);
        }

        public Assembly LoadFromPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                throw new ArgumentException("Analyzer path cannot be null or empty.", nameof(fullPath));
            }

            lock (_gate)
            {
                if (_loadedAssemblies.TryGetValue(fullPath, out var assembly))
                {
                    return assembly;
                }

                var loaded = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                _loadedAssemblies[fullPath] = loaded;

                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    _directories.Add(directory);
                }

                return loaded;
            }
        }

        private Assembly? OnResolving(AssemblyLoadContext _, AssemblyName assemblyName)
        {
            lock (_gate)
            {
                foreach (var directory in _directories)
                {
                    var candidate = Path.Combine(directory, assemblyName.Name + ".dll");
                    if (_loadedAssemblies.TryGetValue(candidate, out var existing))
                    {
                        return existing;
                    }

                    if (File.Exists(candidate))
                    {
                        return LoadFromPath(candidate);
                    }
                }
            }

            return null;
        }
    }
}
