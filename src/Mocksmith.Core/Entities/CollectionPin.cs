namespace Mocksmith.Core.Entities;

public enum PinMode
{
    Include = 0,
    Exclude = 1,
}

/// <summary>Manual override of a collection's query-driven membership.</summary>
public class CollectionPin
{
    public int CollectionId { get; set; }
    public Collection? Collection { get; set; }

    public Guid SampleId { get; set; }
    public Sample? Sample { get; set; }

    public PinMode Mode { get; set; }
}
