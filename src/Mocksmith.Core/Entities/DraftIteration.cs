namespace Mocksmith.Core.Entities;

/// <summary>One generated draft page within a session (a fan-out candidate or a refine step).</summary>
public class DraftIteration
{
    public Guid Id { get; set; }
    public Guid DraftSessionId { get; set; }
    public DraftSession? DraftSession { get; set; }

    /// <summary>Sequential position within the session.</summary>
    public int Index { get; set; }

    /// <summary>Groups fan-out candidates produced by the same request.</summary>
    public int CandidateGroup { get; set; }

    /// <summary>The refine instruction that produced this iteration; null for initial candidates.</summary>
    public string? InstructionText { get; set; }

    public required string HtmlFile { get; set; }
    public required string Model { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Model-proposed sample name, prefilled into the save dialog.</summary>
    public string? Name { get; set; }

    /// <summary>Model-proposed summary, prefilled into the save dialog.</summary>
    public string? Summary { get; set; }

    /// <summary>Model-suggested tags as a JSON string array, shown as approval chips.</summary>
    public string? TagsJson { get; set; }
}
