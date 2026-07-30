namespace Mocksmith.Core.Entities;

/// <summary>A saved design sample: one self-contained HTML page plus metadata.</summary>
public class Sample
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string Summary { get; set; } = "";

    /// <summary>The original request text that produced this sample.</summary>
    public string Description { get; set; } = "";

    /// <summary>Provenance link to the site that inspired the sample, if any.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Path of the sample HTML relative to the data root (e.g. samples/{id}/sample.html).</summary>
    public required string HtmlFile { get; set; }

    /// <summary>Cached token manifest parsed from the sample's mocksmith-tokens block.</summary>
    public string? TokensJson { get; set; }

    /// <summary>Claude model that generated the sample, when generated in-app.</summary>
    public string? Model { get; set; }

    /// <summary>Cached AI-written design brief (markdown); null until generated.</summary>
    public string? BriefMarkdown { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<SampleTag> SampleTags { get; set; } = [];
    public List<Variant> Variants { get; set; } = [];
    public List<InputAsset> Assets { get; set; } = [];
}
