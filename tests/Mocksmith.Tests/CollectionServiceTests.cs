using Mocksmith.Core.Entities;
using Mocksmith.Core.Services;

namespace Mocksmith.Tests;

public class CollectionServiceTests : IDisposable
{
    private readonly SqliteContextFactory _factory = new();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mocksmith-collection-tests-{Guid.NewGuid():N}");
    private readonly SampleImportService _importService;
    private readonly CollectionService _service;

    public CollectionServiceTests()
    {
        Directory.CreateDirectory(_tempRoot);
        var fileStore = new SampleFileStore(new MocksmithDataOptions { RootPath = _tempRoot });
        _importService = new SampleImportService(_factory, fileStore, TimeProvider.System);
        _service = new CollectionService(_factory);
    }

    private Task<Sample> AddSampleAsync(string name, params string[] tags)
        => _importService.ImportAsync(name, "", tags, "<html></html>");

    [Fact]
    public async Task Members_FollowQueryWithAndSemantics()
    {
        var both = await AddSampleAsync("Both", "dark", "dashboard");
        await AddSampleAsync("OnlyDark", "dark");
        var collection = await _service.CreateAsync("Dark dashboards", "dark AND dashboard");

        var members = await _service.GetMembersAsync(collection.Id);

        Assert.Equal(both.Id, Assert.Single(members).Id);
    }

    [Fact]
    public async Task Membership_TracksTagChangesLive()
    {
        await AddSampleAsync("A", "dark");
        var collection = await _service.CreateAsync("Dark", "dark AND dashboard");
        Assert.Empty(await _service.GetMembersAsync(collection.Id));

        // A new sample carrying both tags joins with no collection edits.
        await AddSampleAsync("B", "dark", "dashboard");
        Assert.Single(await _service.GetMembersAsync(collection.Id));
    }

    [Fact]
    public async Task ExcludePin_RemovesQueryMatch_AndIncludePin_AddsUnmatched()
    {
        var matched = await AddSampleAsync("Matched", "dark");
        var unmatched = await AddSampleAsync("Unmatched", "light");
        var collection = await _service.CreateAsync("Dark", "dark");

        await _service.SetPinAsync(collection.Id, matched.Id, PinMode.Exclude);
        await _service.SetPinAsync(collection.Id, unmatched.Id, PinMode.Include);

        var members = await _service.GetMembersAsync(collection.Id);
        Assert.Equal(unmatched.Id, Assert.Single(members).Id);
    }

    [Fact]
    public async Task RemovingPin_RestoresQueryMembership()
    {
        var matched = await AddSampleAsync("Matched", "dark");
        var collection = await _service.CreateAsync("Dark", "dark");
        await _service.SetPinAsync(collection.Id, matched.Id, PinMode.Exclude);
        Assert.Empty(await _service.GetMembersAsync(collection.Id));

        await _service.SetPinAsync(collection.Id, matched.Id, null);

        Assert.Single(await _service.GetMembersAsync(collection.Id));
    }

    [Fact]
    public async Task Counts_ReflectPinAdjustedMembership()
    {
        var a = await AddSampleAsync("A", "dark");
        await AddSampleAsync("B", "dark");
        var collection = await _service.CreateAsync("Dark", "dark");
        await _service.SetPinAsync(collection.Id, a.Id, PinMode.Exclude);

        var entry = Assert.Single(await _service.GetAllWithCountsAsync());
        Assert.Equal(1, entry.MemberCount);
    }

    [Fact]
    public async Task Create_InvalidQuery_ThrowsWithMessage()
    {
        var ex = await Assert.ThrowsAsync<FormatException>(() => _service.CreateAsync("Bad", "dark AND"));
        Assert.NotEmpty(ex.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync("  ", "dark"));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_tempRoot, recursive: true);
    }
}
