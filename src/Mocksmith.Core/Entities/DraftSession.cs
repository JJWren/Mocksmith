namespace Mocksmith.Core.Entities;

public enum DraftSessionStatus
{
    Active = 0,
    Saved = 1,
    Discarded = 2,
}

/// <summary>A generation workspace session holding candidate drafts and refine iterations.</summary>
public class DraftSession
{
    public Guid Id { get; set; }
    public DraftSessionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<DraftIteration> Iterations { get; set; } = [];
    public List<InputAsset> Assets { get; set; } = [];
}
