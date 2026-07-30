using Mocksmith.Core.Entities;
using Mocksmith.Core.Services;

namespace Mocksmith.Tests;

public class UsageServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly SqliteContextFactory _factory = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly UsageService _service;

    public UsageServiceTests()
    {
        _service = new UsageService(_factory, _clock);
        using var db = _factory.CreateDbContext();
        db.GenerationLogs.AddRange(
            new GenerationLog { Model = "claude-sonnet-5", Backend = "api", InputTokens = 1000, OutputTokens = 2000, EstimatedCostUsd = 0.033m, DurationMs = 5000, CreatedAt = new DateTime(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc) },
            new GenerationLog { Model = "claude-sonnet-5", Backend = "claude-code", InputTokens = 500, OutputTokens = 1500, EstimatedCostUsd = null, DurationMs = 30000, CreatedAt = new DateTime(2026, 7, 30, 9, 0, 0, DateTimeKind.Utc) },
            new GenerationLog { Model = "claude-haiku-4-5", Backend = "api", InputTokens = 200, OutputTokens = 300, EstimatedCostUsd = 0.0017m, DurationMs = 2000, CreatedAt = new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc) });
        db.SaveChanges();
    }

    [Fact]
    public async Task Summary_AggregatesTotalsAndMonth()
    {
        var (totals, byModel) = await _service.GetSummaryAsync();

        Assert.Equal(3, totals.Calls);
        Assert.Equal(1700, totals.InputTokens);
        Assert.Equal(3800, totals.OutputTokens);
        Assert.Equal(0.0347m, totals.ApiCostUsd);
        // July 2026: two calls, only the api one carries dollar cost.
        Assert.Equal(2, totals.MonthCalls);
        Assert.Equal(0.033m, totals.MonthApiCostUsd);

        Assert.Equal(2, byModel.Count(m => m.Model == "claude-sonnet-5"));
        var subscription = Assert.Single(byModel, m => m.Backend == "claude-code");
        Assert.Equal(0m, subscription.ApiCostUsd);
    }

    [Fact]
    public async Task Recent_OrdersNewestFirst()
    {
        var recent = await _service.GetRecentAsync();

        Assert.Equal(3, recent.Count);
        Assert.Equal("claude-code", recent[0].Backend);
    }

    public void Dispose() => _factory.Dispose();
}
