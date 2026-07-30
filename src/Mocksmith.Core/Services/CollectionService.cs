using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Data;
using Mocksmith.Core.Entities;

namespace Mocksmith.Core.Services;

public record CollectionWithCount(Collection Collection, int MemberCount);

/// <summary>
/// Smart collections with manual overrides. Membership is one documented function:
/// <c>member(sample) = (query(sample.tags) AND NOT excluded(sample)) OR included(sample)</c>.
/// </summary>
public class CollectionService(IDbContextFactory<MocksmithDbContext> contextFactory)
{
    public async Task<Collection> CreateAsync(string name, string tagQuery, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A collection name is required.", nameof(name));
        }

        TagQuery.Parse(tagQuery); // throws FormatException with a readable message

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var collection = new Collection { Name = name.Trim(), TagQuery = tagQuery.Trim() };
        db.Collections.Add(collection);
        await db.SaveChangesAsync(ct);
        return collection;
    }

    public async Task UpdateAsync(int collectionId, string name, string tagQuery, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A collection name is required.", nameof(name));
        }

        TagQuery.Parse(tagQuery);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId, ct)
            ?? throw new InvalidOperationException("Collection not found.");
        collection.Name = name.Trim();
        collection.TagQuery = tagQuery.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int collectionId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await db.Collections.Where(c => c.Id == collectionId).ExecuteDeleteAsync(ct);
    }

    public async Task<Collection?> GetAsync(int collectionId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Collections.AsNoTracking()
            .Include(c => c.Pins)
            .FirstOrDefaultAsync(c => c.Id == collectionId, ct);
    }

    public async Task<List<CollectionWithCount>> GetAllWithCountsAsync(CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var collections = await db.Collections.AsNoTracking().Include(c => c.Pins).OrderBy(c => c.Name).ToListAsync(ct);
        if (collections.Count == 0)
        {
            return [];
        }

        var samples = await LoadSampleTagSetsAsync(db, ct);
        return collections
            .Select(collection => new CollectionWithCount(collection, MemberIds(collection, samples).Count))
            .ToList();
    }

    /// <summary>Members ordered newest-first, ready for the tile grid.</summary>
    public async Task<List<Sample>> GetMembersAsync(int collectionId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var collection = await db.Collections.AsNoTracking().Include(c => c.Pins)
            .FirstOrDefaultAsync(c => c.Id == collectionId, ct)
            ?? throw new InvalidOperationException("Collection not found.");

        var samples = await LoadSampleTagSetsAsync(db, ct);
        var memberIds = MemberIds(collection, samples);
        return await db.Samples.AsNoTracking()
            .Include(s => s.SampleTags).ThenInclude(st => st.Tag)
            .Include(s => s.Variants)
            .Where(s => memberIds.Contains(s.Id))
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);
    }

    /// <summary>Sets a pin (Include/Exclude) or removes it when <paramref name="mode"/> is null.</summary>
    public async Task SetPinAsync(int collectionId, Guid sampleId, PinMode? mode, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var pin = await db.CollectionPins
            .FirstOrDefaultAsync(p => p.CollectionId == collectionId && p.SampleId == sampleId, ct);
        if (mode is null)
        {
            if (pin is not null)
            {
                db.CollectionPins.Remove(pin);
            }
        }
        else if (pin is null)
        {
            db.CollectionPins.Add(new CollectionPin { CollectionId = collectionId, SampleId = sampleId, Mode = mode.Value });
        }
        else
        {
            pin.Mode = mode.Value;
        }

        await db.SaveChangesAsync(ct);
    }

    private static HashSet<Guid> MemberIds(
        Collection collection,
        IReadOnlyList<(Guid Id, HashSet<string> Tags)> samples)
    {
        var query = TagQuery.Parse(collection.TagQuery);
        var included = collection.Pins.Where(p => p.Mode == PinMode.Include).Select(p => p.SampleId).ToHashSet();
        var excluded = collection.Pins.Where(p => p.Mode == PinMode.Exclude).Select(p => p.SampleId).ToHashSet();

        var members = new HashSet<Guid>();
        foreach (var (id, tags) in samples)
        {
            // member = (query AND NOT excluded) OR included
            if ((query.Matches(tags) && !excluded.Contains(id)) || included.Contains(id))
            {
                members.Add(id);
            }
        }

        return members;
    }

    private static async Task<List<(Guid, HashSet<string>)>> LoadSampleTagSetsAsync(
        MocksmithDbContext db,
        CancellationToken ct)
    {
        var rows = await db.Samples.AsNoTracking()
            .Select(s => new { s.Id, Tags = s.SampleTags.Select(st => st.Tag!.Name).ToList() })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Tags.ToHashSet(StringComparer.Ordinal))).ToList();
    }
}
