using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Data;
using Mocksmith.Core.Entities;

namespace Mocksmith.Core.Services;

public record UsageTotals(
    int Calls,
    long InputTokens,
    long OutputTokens,
    decimal ApiCostUsd,
    int MonthCalls,
    decimal MonthApiCostUsd);

public record ModelUsage(string Model, string Backend, int Calls, long InputTokens, long OutputTokens, decimal ApiCostUsd);

/// <summary>
/// Cost/usage reporting over GenerationLog. Aggregation happens in memory: the SQLite
/// provider cannot translate decimal aggregates, and the log volume is personal-scale.
/// </summary>
public class UsageService(IDbContextFactory<MocksmithDbContext> contextFactory, TimeProvider timeProvider)
{
    public async Task<List<GenerationLog>> GetRecentAsync(int limit = 200, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.GenerationLogs.AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<(UsageTotals Totals, List<ModelUsage> ByModel)> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var rows = await db.GenerationLogs.AsNoTracking().ToListAsync(ct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthRows = rows.Where(r => r.CreatedAt >= monthStart).ToList();

        var totals = new UsageTotals(
            rows.Count,
            rows.Sum(r => (long)r.InputTokens),
            rows.Sum(r => (long)r.OutputTokens),
            rows.Sum(r => r.EstimatedCostUsd ?? 0m),
            monthRows.Count,
            monthRows.Sum(r => r.EstimatedCostUsd ?? 0m));

        var byModel = rows
            .GroupBy(r => (r.Model, r.Backend))
            .Select(g => new ModelUsage(
                g.Key.Model,
                g.Key.Backend,
                g.Count(),
                g.Sum(r => (long)r.InputTokens),
                g.Sum(r => (long)r.OutputTokens),
                g.Sum(r => r.EstimatedCostUsd ?? 0m)))
            .OrderByDescending(m => m.Calls)
            .ThenBy(m => m.Model, StringComparer.Ordinal)
            .ThenBy(m => m.Backend, StringComparer.Ordinal)
            .ToList();

        return (totals, byModel);
    }
}
