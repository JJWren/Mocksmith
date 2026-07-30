using Mocksmith.Core.Entities;
using Mocksmith.Core.Services;

namespace Mocksmith.Tests;

public class SampleQueryServiceTests : IDisposable
{
    private readonly SqliteContextFactory _factory = new();
    private readonly SampleQueryService _service;

    public SampleQueryServiceTests()
    {
        _service = new SampleQueryService(_factory);
        Seed();
    }

    private void Seed()
    {
        using var db = _factory.CreateDbContext();
        var dark = new Tag { Name = "dark" };
        var dashboard = new Tag { Name = "dashboard" };
        var retro = new Tag { Name = "retro" };

        db.Samples.AddRange(
            NewSample("Midnight Analytics", "A dark analytics dashboard", daysAgo: 2, dark, dashboard),
            NewSample("Sunrise Landing", "Warm marketing landing page", daysAgo: 1, retro),
            NewSample("50% Off Banner", "Promo banner concepts", daysAgo: 0, dark));
        db.SaveChanges();
    }

    private static Sample NewSample(string name, string summary, int daysAgo, params Tag[] tags)
    {
        var sample = new Sample
        {
            Id = Guid.NewGuid(),
            Name = name,
            Summary = summary,
            HtmlFile = $"samples/{Guid.NewGuid()}/sample.html",
            CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
            UpdatedAt = DateTime.UtcNow.AddDays(-daysAgo),
        };
        foreach (var tag in tags)
        {
            sample.SampleTags.Add(new SampleTag { Sample = sample, Tag = tag });
        }

        return sample;
    }

    [Fact]
    public async Task Search_NoFilter_ReturnsAllNewestFirst()
    {
        var results = await _service.SearchAsync(new SampleFilter());

        Assert.Equal(3, results.Count);
        Assert.Equal("50% Off Banner", results[0].Name);
    }

    [Fact]
    public async Task Search_Text_MatchesNameOrSummary_CaseInsensitive()
    {
        Assert.Single(await _service.SearchAsync(new SampleFilter(Text: "midnight")));
        Assert.Single(await _service.SearchAsync(new SampleFilter(Text: "MARKETING")));
    }

    [Fact]
    public async Task Search_SingleTag_FiltersToTaggedSamples()
    {
        var results = await _service.SearchAsync(new SampleFilter(Tags: ["dark"]));

        Assert.Equal(2, results.Count);
        Assert.All(results, s => Assert.Contains(s.SampleTags, st => st.Tag!.Name == "dark"));
    }

    [Fact]
    public async Task Search_MultipleTags_UseAndSemantics()
    {
        var results = await _service.SearchAsync(new SampleFilter(Tags: ["dark", "dashboard"]));

        Assert.Single(results);
        Assert.Equal("Midnight Analytics", results[0].Name);
    }

    [Fact]
    public async Task Search_TextAndTag_Combine()
    {
        var results = await _service.SearchAsync(new SampleFilter(Text: "banner", Tags: ["dark"]));

        Assert.Single(results);
        Assert.Equal("50% Off Banner", results[0].Name);
    }

    [Fact]
    public async Task Search_LikeWildcardsInText_AreEscaped()
    {
        var results = await _service.SearchAsync(new SampleFilter(Text: "50%"));

        Assert.Single(results);
        Assert.Equal("50% Off Banner", results[0].Name);

        Assert.Empty(await _service.SearchAsync(new SampleFilter(Text: "5_%")));
    }

    public void Dispose() => _factory.Dispose();
}
