namespace RoslynMcp.Tools.Inspection.LoadSolution;

internal static class Extensions
{
    extension(string? solutionHintPath)
    {
        public Request ToRequest() => new(solutionHintPath?.NormalizeOptional());
    }
}