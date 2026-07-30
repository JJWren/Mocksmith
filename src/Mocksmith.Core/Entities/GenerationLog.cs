namespace Mocksmith.Core.Entities;

/// <summary>Usage/cost record for every Claude API call.</summary>
public class GenerationLog
{
    public long Id { get; set; }
    public required string Model { get; set; }

    /// <summary>"api" or "claude-code".</summary>
    public string Backend { get; set; } = "";

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>Null for subscription-backed (claude-code) runs.</summary>
    public decimal? EstimatedCostUsd { get; set; }

    public long DurationMs { get; set; }

    public Guid? DraftSessionId { get; set; }
    public DateTime CreatedAt { get; set; }
}
