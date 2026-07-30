namespace Mocksmith.Core.Entities;

/// <summary>Kebab-case tag; the primary navigation axis of the catalog.</summary>
public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public List<SampleTag> SampleTags { get; set; } = [];
}
