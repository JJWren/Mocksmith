namespace Mocksmith.Core.Entities;

/// <summary>Single-row application settings (Id is always 1).</summary>
public class AppSettings
{
    public int Id { get; set; }

    /// <summary>Default Claude model used for generation requests.</summary>
    public required string DefaultModel { get; set; }
}
