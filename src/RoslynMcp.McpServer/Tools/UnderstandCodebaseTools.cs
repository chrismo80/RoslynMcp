using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class UnderstandCodebaseTools
{
    private readonly ICodeUnderstandingService _codeUnderstandingService;

    public UnderstandCodebaseTools(ICodeUnderstandingService codeUnderstandingService)
    {
        _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));
    }

    [McpServerTool(Name = "understand_codebase", Title = "Understand Codebase", ReadOnly = true, Idempotent = true)]
    [Description("Quick codebase orientation: returns project structure with dependencies and hotspots (most complex/commented methods). Use at session start to identify high-impact areas. Profiles: quick (fast), standard (balanced), deep (thorough).")]
    public Task<UnderstandCodebaseResult> UnderstandCodebaseAsync(
        CancellationToken cancellationToken,
        [Description("Hotspot profile: quick, standard, or deep. Defaults to standard; unsupported values are treated as standard.")]
        string? profile = null)
        => _codeUnderstandingService.UnderstandCodebaseAsync(ToolContractMapper.ToUnderstandCodebaseRequest(profile), cancellationToken);
}
