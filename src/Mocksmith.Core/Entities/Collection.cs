namespace Mocksmith.Core.Entities;

/// <summary>
/// A smart collection: membership is a saved tag query (e.g. "dark AND dashboard"),
/// adjusted by manual include/exclude pins.
/// </summary>
public class Collection
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string TagQuery { get; set; }

    public List<CollectionPin> Pins { get; set; } = [];
}
