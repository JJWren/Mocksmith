namespace Mocksmith.Core.Generation;

public record ScreenshotInput(string FileName, string MediaType, byte[] Data);

/// <summary>One generation or refine turn. When <see cref="CurrentHtml"/> is set this is a refine.</summary>
public record DesignGenerationRequest
{
    public required string Description { get; init; }
    public IReadOnlyList<ScreenshotInput> Screenshots { get; init; } = [];

    /// <summary>Provenance URL; when present the API backend enables the web_fetch server tool.</summary>
    public string? SourceUrl { get; init; }

    public required string Model { get; init; }

    /// <summary>Existing tag vocabulary, injected so the model reuses established tags.</summary>
    public IReadOnlyList<string> ExistingTags { get; init; } = [];

    public string? CurrentHtml { get; init; }
    public string? Instruction { get; init; }

    public bool IsRefine => CurrentHtml is not null;
}

public record DesignGenerationResult
{
    public required string Name { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required string Html { get; init; }
    public required string Model { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    /// <summary>Null when the run is subscription-backed (Claude Code CLI).</summary>
    public decimal? EstimatedCostUsd { get; init; }

    public long DurationMs { get; init; }
}

public record GenerationProgress(string Phase, int OutputTokensSoFar = 0);

public interface IDesignGenerator
{
    /// <summary>"api" or "claude-code" — surfaced in the UI and logs.</summary>
    string BackendName { get; }

    Task<DesignGenerationResult> GenerateAsync(
        DesignGenerationRequest request,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken ct = default);
}
