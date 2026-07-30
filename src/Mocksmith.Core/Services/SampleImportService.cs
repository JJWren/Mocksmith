using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Data;
using Mocksmith.Core.Entities;

namespace Mocksmith.Core.Services;

/// <summary>Creates samples from externally produced HTML (dev aid and generation-independent path).</summary>
public class SampleImportService(
    IDbContextFactory<MocksmithDbContext> contextFactory,
    SampleFileStore fileStore,
    TimeProvider timeProvider)
{
    public async Task<Sample> ImportAsync(
        string name,
        string summary,
        IEnumerable<string> tags,
        string html,
        string? sourceUrl = null,
        string? description = null,
        string? model = null,
        string? tokensJson = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A sample name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ArgumentException("Sample HTML is required.", nameof(html));
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sample = new Sample
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Summary = summary.Trim(),
            Description = description?.Trim() ?? "",
            SourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl.Trim(),
            Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
            TokensJson = tokensJson,
            HtmlFile = "",
            CreatedAt = now,
            UpdatedAt = now,
        };
        sample.HtmlFile = await fileStore.WriteSampleHtmlAsync(sample.Id, html, ct);

        var tagNames = TagNormalizer.NormalizeSet(tags);
        var existing = await db.Tags.Where(t => tagNames.Contains(t.Name)).ToListAsync(ct);
        foreach (var tagName in tagNames)
        {
            var tag = existing.FirstOrDefault(t => t.Name == tagName)
                ?? new Tag { Name = tagName };
            sample.SampleTags.Add(new SampleTag { Sample = sample, Tag = tag });
        }

        db.Samples.Add(sample);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Compensate the file write above so a failed save leaves no orphan on disk.
            fileStore.TryDelete(sample.HtmlFile);
            throw;
        }

        return sample;
    }
}
