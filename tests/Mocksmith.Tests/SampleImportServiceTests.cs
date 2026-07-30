using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Services;

namespace Mocksmith.Tests;

public class SampleImportServiceTests : IDisposable
{
    private readonly SqliteContextFactory _factory = new();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mocksmith-tests-{Guid.NewGuid():N}");
    private readonly SampleImportService _service;
    private readonly SampleFileStore _fileStore;

    public SampleImportServiceTests()
    {
        Directory.CreateDirectory(_tempRoot);
        _fileStore = new SampleFileStore(new MocksmithDataOptions { RootPath = _tempRoot });
        _service = new SampleImportService(_factory, _fileStore, TimeProvider.System);
    }

    [Fact]
    public async Task Import_WritesFile_AndPersistsSampleWithNormalizedTags()
    {
        var sample = await _service.ImportAsync(
            "  Neon Grid  ",
            "A neon-soaked grid layout",
            ["Dark Mode", "neon"],
            "<!doctype html><html><body>hi</body></html>");

        Assert.Equal("Neon Grid", sample.Name);
        Assert.True(File.Exists(Path.Combine(_tempRoot, "samples", sample.Id.ToString(), "sample.html")));

        await using var db = _factory.CreateDbContext();
        var persisted = await db.Samples
            .Include(s => s.SampleTags).ThenInclude(st => st.Tag)
            .SingleAsync(s => s.Id == sample.Id);
        Assert.Equal(["dark-mode", "neon"], persisted.SampleTags.Select(st => st.Tag!.Name).Order().ToArray());
    }

    [Fact]
    public async Task Import_ReusesExistingTagRows()
    {
        await _service.ImportAsync("First", "", ["dark"], "<html></html>");
        await _service.ImportAsync("Second", "", ["Dark", "new-tag"], "<html></html>");

        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, await db.Tags.CountAsync());
    }

    [Theory]
    [InlineData("", "<html></html>")]
    [InlineData("Name", "")]
    public async Task Import_MissingNameOrHtml_Throws(string name, string html)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ImportAsync(name, "", [], html));
    }

    [Fact]
    public void FileStore_RejectsPathTraversal()
    {
        Assert.Throws<InvalidOperationException>(() => _fileStore.ResolveAbsolute("../outside.html"));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_tempRoot, recursive: true);
    }
}
