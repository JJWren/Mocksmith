namespace Mocksmith.Core.Entities;

/// <summary>An uploaded/pasted screenshot used as generation input and kept as provenance.</summary>
public class InputAsset
{
    public Guid Id { get; set; }

    public Guid? DraftSessionId { get; set; }
    public DraftSession? DraftSession { get; set; }

    public Guid? SampleId { get; set; }
    public Sample? Sample { get; set; }

    public required string FileName { get; set; }

    /// <summary>Path relative to the data root (assets/{id}.{ext}).</summary>
    public required string FilePath { get; set; }

    public required string ContentType { get; set; }
    public DateTime CreatedAt { get; set; }
}
