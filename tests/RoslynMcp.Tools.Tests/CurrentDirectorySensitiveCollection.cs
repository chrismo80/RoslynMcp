using Xunit;

namespace RoslynMcp.Tools.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CurrentDirectorySensitiveCollection
{
    public const string Name = "CurrentDirectorySensitive";
}
