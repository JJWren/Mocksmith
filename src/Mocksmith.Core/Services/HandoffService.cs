using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Data;
using Mocksmith.Core.Entities;
using Mocksmith.Core.Generation;

namespace Mocksmith.Core.Services;

/// <summary>
/// Builds the two handoff forms from the same data: a downloadable zip bundle
/// (sample.html, design-tokens.json, design-brief.md, metadata.json, source screenshots)
/// and a single copy-as-agent-prompt markdown blob.
/// </summary>
public partial class HandoffService(
    IDbContextFactory<MocksmithDbContext> contextFactory,
    SampleFileStore fileStore,
    IDesignGenerator generator,
    TimeProvider timeProvider)
{
    [GeneratedRegex(@"(--[A-Za-z0-9\-_]+)\s*:\s*([^;{}]+);")]
    private static partial Regex RootTokenValueRegex();

    public async Task<(byte[] Content, string FileName)> BuildBundleAsync(
        Guid sampleId,
        Guid? variantId = null,
        CancellationToken ct = default)
    {
        var (sample, variant, html) = await LoadAsync(sampleId, variantId, ct);
        var tags = sample.SampleTags.Select(st => st.Tag!.Name).ToList();
        var tokensJson = PrettyTokens(html, sample);
        var brief = SelectBrief(sample, variant, html, tags);
        var metadata = BuildMetadataJson(sample, variant, tags);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(zip, "sample.html", html, ct);
            await WriteEntryAsync(zip, "design-tokens.json", tokensJson, ct);
            await WriteEntryAsync(zip, "design-brief.md", brief, ct);
            await WriteEntryAsync(zip, "metadata.json", metadata, ct);
            foreach (var asset in sample.Assets)
            {
                if (fileStore.Exists(asset.FilePath))
                {
                    var entry = zip.CreateEntry($"assets/{Path.GetFileName(asset.FilePath)}");
                    await using var stream = entry.Open();
                    var bytes = await fileStore.ReadBytesAsync(asset.FilePath, ct);
                    await stream.WriteAsync(bytes, ct);
                }
            }
        }

        var fileName = variant is null
            ? $"{Slug(sample.Name)}-handoff.zip"
            : $"{Slug(sample.Name)}--{Slug(variant.Name)}-handoff.zip";
        return (buffer.ToArray(), fileName);
    }

    public async Task<string> BuildAgentPromptAsync(
        Guid sampleId,
        Guid? variantId = null,
        CancellationToken ct = default)
    {
        var (sample, variant, html) = await LoadAsync(sampleId, variantId, ct);
        var tags = sample.SampleTags.Select(st => st.Tag!.Name).ToList();
        var brief = SelectBrief(sample, variant, html, tags);

        var builder = new StringBuilder();
        builder.AppendLine("# Design handoff: implement this design in a real application");
        builder.AppendLine();
        builder.AppendLine("You are implementing the design demonstrated by the sample below. Reproduce its");
        builder.AppendLine("visual system faithfully: use the design tokens as your styling source of truth,");
        builder.AppendLine("keep the typography and spacing relationships, and adapt components as needed.");
        builder.AppendLine();
        builder.AppendLine(brief);
        builder.AppendLine();
        builder.AppendLine("## Design tokens");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine(PrettyTokens(html, sample));
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Reference sample (complete, self-contained)");
        builder.AppendLine();
        builder.AppendLine("```html");
        builder.AppendLine(html);
        builder.AppendLine("```");
        return builder.ToString();
    }

    /// <summary>Writes a fresh AI brief via the active backend, caches it, and logs the call.</summary>
    public async Task<string> RegenerateBriefAsync(Guid sampleId, CancellationToken ct = default)
    {
        var (sample, _, html) = await LoadAsync(sampleId, variantId: null, ct);
        var tags = sample.SampleTags.Select(st => st.Tag!.Name).ToList();

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);
        var model = sample.Model ?? settings.DefaultModel;

        var result = await generator.GenerateBriefAsync(
            new BriefRequest(html, sample.Name, sample.Summary, sample.Description, tags, model), ct);

        db.GenerationLogs.Add(new GenerationLog
        {
            Model = model,
            Backend = generator.BackendName,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            EstimatedCostUsd = result.EstimatedCostUsd,
            DurationMs = result.DurationMs,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
        await db.Samples
            .Where(s => s.Id == sampleId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.BriefMarkdown, result.Markdown), ct);
        await db.SaveChangesAsync(ct);
        return result.Markdown;
    }

    /// <summary>
    /// The cached AI brief describes the base sample only; variant exports always use the
    /// template brief, which is built from the variant's own HTML and cannot contradict it.
    /// </summary>
    private static string SelectBrief(Sample sample, Variant? variant, string html, IReadOnlyList<string> tags)
        => variant is null && sample.BriefMarkdown is not null
            ? sample.BriefMarkdown
            : BuildTemplateBrief(sample, variant, html, tags);

    /// <summary>Deterministic brief assembled from stored data — used until an AI brief is cached.</summary>
    public static string BuildTemplateBrief(Sample sample, Variant? variant, string html, IReadOnlyList<string> tags)
    {
        var tokens = RootTokenValueRegex().Matches(html)
            .Select(m => (Name: m.Groups[1].Value, Value: m.Groups[2].Value.Trim()))
            .DistinctBy(t => t.Name)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine($"# Design brief — {sample.Name}{(variant is null ? "" : $" · {variant.Name} variant")}");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(sample.Summary))
        {
            builder.AppendLine(sample.Summary);
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(sample.Description))
        {
            builder.AppendLine("## Intent");
            builder.AppendLine();
            builder.AppendLine(sample.Description);
            builder.AppendLine();
        }

        if (tags.Count > 0)
        {
            builder.AppendLine($"**Tags:** {string.Join(", ", tags)}");
            builder.AppendLine();
        }

        if (tokens.Count > 0)
        {
            builder.AppendLine("## Design tokens");
            builder.AppendLine();
            builder.AppendLine("| Token | Value |");
            builder.AppendLine("|---|---|");
            foreach (var (name, value) in tokens)
            {
                builder.AppendLine($"| `{name}` | `{value}` |");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Implementation notes");
        builder.AppendLine();
        builder.AppendLine("- The sample is a single self-contained HTML page; every major design decision is a");
        builder.AppendLine("  CSS custom property on `:root` (see the token table and `design-tokens.json`).");
        builder.AppendLine("- Reference tokens rather than literals when implementing, so theming stays coherent.");
        builder.AppendLine("- Interactive states (hover/focus/menus) are vanilla CSS/JS inside the sample.");
        builder.AppendLine();
        builder.AppendLine("## Provenance");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(sample.SourceUrl))
        {
            builder.AppendLine($"- Inspiration source: {sample.SourceUrl}");
        }

        if (!string.IsNullOrWhiteSpace(sample.Model))
        {
            builder.AppendLine($"- Generated with: {sample.Model}");
        }

        builder.AppendLine($"- Created: {sample.CreatedAt:yyyy-MM-dd}; last updated: {sample.UpdatedAt:yyyy-MM-dd}");
        return builder.ToString();
    }

    private async Task<(Sample Sample, Variant? Variant, string Html)> LoadAsync(
        Guid sampleId,
        Guid? variantId,
        CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var sample = await db.Samples.AsNoTracking()
            .Include(s => s.SampleTags).ThenInclude(st => st.Tag)
            .Include(s => s.Assets)
            .FirstOrDefaultAsync(s => s.Id == sampleId, ct)
            ?? throw new InvalidOperationException($"Sample {sampleId} not found.");

        Variant? variant = null;
        if (variantId is { } vid)
        {
            variant = await db.Variants.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == vid && v.SampleId == sampleId, ct)
                ?? throw new InvalidOperationException($"Variant {vid} not found on this sample.");
        }

        var htmlFile = variant?.HtmlFile ?? sample.HtmlFile;
        if (!fileStore.Exists(htmlFile))
        {
            throw new InvalidOperationException($"Stored HTML for {(variant is null ? "sample" : "variant")} is missing on disk.");
        }

        var html = await fileStore.ReadTextAsync(htmlFile, ct);
        return (sample, variant, html);
    }

    private static string PrettyTokens(string html, Sample sample)
    {
        var raw = TokenContractValidator.TryExtractManifestJson(html) ?? sample.TokensJson;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "{}";
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static string BuildMetadataJson(Sample sample, Variant? variant, IReadOnlyList<string> tags)
        => JsonSerializer.Serialize(new
        {
            name = sample.Name,
            summary = sample.Summary,
            description = sample.Description,
            tags,
            sourceUrl = sample.SourceUrl,
            model = sample.Model,
            createdAt = sample.CreatedAt,
            updatedAt = sample.UpdatedAt,
            variant = variant is null
                ? null
                : new { name = variant.Name, updatedAt = variant.UpdatedAt, patchCss = variant.PatchJson },
        }, new JsonSerializerOptions { WriteIndented = true });

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), ct);
    }

    private static string Slug(string value)
    {
        var normalized = TagNormalizer.Normalize(value);
        return normalized.Length > 0 ? normalized : "sample";
    }
}
