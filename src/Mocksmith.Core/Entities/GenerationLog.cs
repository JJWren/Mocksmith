namespace Mocksmith.Core.Entities;

/// <summary>Usage/cost record for every Claude API call.</summary>
public class GenerationLog
{
    public long Id { get; set; }
    public required string Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public long DurationMs { get; set; }

    public Guid? DraftSessionId { get; set; }
    public DateTime CreatedAt { get; set; }
}
