namespace Mocksmith.Core.Entities;

/// <summary>
/// A named fork of a sample (e.g. "Dark", "Compact"). The name is an upsert key:
/// saving an existing name overwrites that variant.
/// </summary>
public class Variant
{
    public Guid Id { get; set; }
    public Guid SampleId { get; set; }
    public Sample? Sample { get; set; }

    public required string Name { get; set; }

    /// <summary>Materialized standalone HTML, relative to the data root.</summary>
    public required string HtmlFile { get; set; }

    /// <summary>Override deltas (selector/property/value patches) kept for provenance.</summary>
    public string? PatchJson { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
