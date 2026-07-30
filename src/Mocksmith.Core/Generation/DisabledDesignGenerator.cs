namespace Mocksmith.Core.Generation;

/// <summary>Placeholder when no backend is configured; the UI hides generation in this state.</summary>
public class DisabledDesignGenerator : IDesignGenerator
{
    public string BackendName => "disabled";

    public Task<DesignGenerationResult> GenerateAsync(
        DesignGenerationRequest request,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken ct = default)
        => throw NotConfigured();

    public Task<BriefResult> GenerateBriefAsync(BriefRequest request, CancellationToken ct = default)
        => throw NotConfigured();

    private static InvalidOperationException NotConfigured()
        => new(
            "No generation backend is configured. Set ANTHROPIC_API_KEY (API) or "
            + "CLAUDE_CODE_OAUTH_TOKEN / install the claude CLI (subscription), "
            + "or set MOCKSMITH_GENERATOR explicitly.");
}
