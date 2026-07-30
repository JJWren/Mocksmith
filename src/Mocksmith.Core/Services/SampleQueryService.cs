using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Data;
using Mocksmith.Core.Entities;

namespace Mocksmith.Core.Services;

public record SampleFilter(string? Text = null, IReadOnlyList<string>? Tags = null);

public record SampleNameEntry(Guid Id, string Name);

/// <summary>Dashboard queries: text + tag filtering (tags combine with AND semantics).</summary>
public class SampleQueryService(IDbContextFactory<MocksmithDbContext> contextFactory)
{
    public async Task<List<Sample>> SearchAsync(SampleFilter filter, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var query = db.Samples
            .AsNoTracking()
            .Include(s => s.SampleTags).ThenInclude(st => st.Tag)
            .Include(s => s.Variants)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = $"%{EscapeLike(filter.Text.Trim())}%";
            query = query.Where(s =>
                EF.Functions.Like(s.Name, pattern, "\\") ||
                EF.Functions.Like(s.Summary, pattern, "\\"));
        }

        foreach (var tag in filter.Tags ?? [])
        {
            var tagName = tag;
            query = query.Where(s => s.SampleTags.Any(st => st.Tag!.Name == tagName));
        }

        return await query.OrderByDescending(s => s.UpdatedAt).ToListAsync(ct);
    }

    public async Task<List<Tag>> GetAllTagsAsync(CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Tags.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
    }

    /// <summary>Lightweight id/name index for pickers — no tags or variants loaded.</summary>
    public async Task<List<SampleNameEntry>> GetNameIndexAsync(CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Samples.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SampleNameEntry(s.Id, s.Name))
            .ToListAsync(ct);
    }

    private static string EscapeLike(string input) =>
        input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
