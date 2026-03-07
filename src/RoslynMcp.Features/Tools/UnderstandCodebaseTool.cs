using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class UnderstandCodebaseTool(ICodeUnderstandingService codeUnderstandingService) : Tool
{
    private readonly ICodeUnderstandingService _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));

    [McpServerTool(Name = "understand_codebase", Title = "Understand Codebase", ReadOnly = true, Idempotent = true)]
        [Description("Use this tool when you need a quick overview of the codebase structure at the start of a session. It returns the project structure with dependency relationships and identifies hotspots from hand-written source by default so generated/intermediate artifacts do not dominate the initial view.")]
    public Task<UnderstandCodebaseResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("Analysis depth. quick for fast results, standard for balanced output, deep for thorough analysis. Defaults to standard.")]
        string? profile = null
        )
        => _codeUnderstandingService.UnderstandCodebaseAsync(profile.ToUnderstandCodebaseRequest(), cancellationToken);
}
