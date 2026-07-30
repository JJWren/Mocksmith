using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Entities;
using Mocksmith.Core.Generation;
using Mocksmith.Core.Services;

namespace Mocksmith.Tests;

public class DraftSessionServiceTests : IDisposable
{
    private readonly SqliteContextFactory _factory = new();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mocksmith-session-tests-{Guid.NewGuid():N}");
    private readonly SampleFileStore _fileStore;
    private readonly SampleImportService _importService;
    private readonly DraftSessionService _service;

    private sealed class StubGenerator : IDesignGenerator
    {
        public string BackendName => "stub";

        public Task<DesignGenerationResult> GenerateAsync(
            DesignGenerationRequest request,
            IProgress<GenerationProgress>? progress = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Not used in these tests.");

        public Task<BriefResult> GenerateBriefAsync(BriefRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("Not used in these tests.");
    }

    public DraftSessionServiceTests()
    {
        Directory.CreateDirectory(_tempRoot);
        _fileStore = new SampleFileStore(new MocksmithDataOptions { RootPath = _tempRoot });
        _importService = new SampleImportService(_factory, _fileStore, TimeProvider.System);
        _service = new DraftSessionService(_factory, new StubGenerator(), _fileStore, _importService, TimeProvider.System);
    }

    [Fact]
    public async Task StartSessionFromSample_SeedsIterationZeroWithSampleContent()
    {
        var sample = await _importService.ImportAsync(
            "Neon Grid", "A neon grid", ["neon", "dark"],
            "<!doctype html><html><head></head><body>SEEDED</body></html>",
            description: "original brief", model: "claude-sonnet-5");

        var session = await _service.StartSessionFromSampleAsync(sample.Id);
        var loaded = await _service.GetSessionAsync(session.Id);

        Assert.NotNull(loaded);
        var iteration = Assert.Single(loaded!.Iterations);
        Assert.True(iteration.IsActive);
        Assert.Equal("Neon Grid", iteration.Name);
        Assert.Contains("neon", iteration.TagsJson);
        Assert.Equal("original brief", loaded.Description);
        Assert.Equal("claude-sonnet-5", loaded.Model);
        Assert.Contains("SEEDED", await _fileStore.ReadTextAsync(iteration.HtmlFile));
    }

    [Fact]
    public async Task ApplyManualPatch_CreatesNewActiveIterationWithBakedCss()
    {
        var sample = await _importService.ImportAsync(
            "Base", "", [],
            "<!doctype html><html><head></head><body><h1>x</h1></body></html>");
        var session = await _service.StartSessionFromSampleAsync(sample.Id);

        await _service.ApplyManualPatchAsync(session.Id, "h1 { color: red; }");

        var loaded = await _service.GetSessionAsync(session.Id);
        Assert.Equal(2, loaded!.Iterations.Count);
        var active = Assert.Single(loaded.Iterations, i => i.IsActive);
        Assert.Equal(1, active.Index);
        Assert.Equal("manual edit (panel)", active.InstructionText);
        var html = await _fileStore.ReadTextAsync(active.HtmlFile);
        Assert.Contains("mocksmith-patch", html);
        Assert.Contains("color: red", html);
    }

    [Fact]
    public async Task ApplyManualPatch_AccumulatesAcrossApplies()
    {
        var sample = await _importService.ImportAsync(
            "Base", "", [],
            "<!doctype html><html><head></head><body><h1>x</h1></body></html>");
        var session = await _service.StartSessionFromSampleAsync(sample.Id);

        await _service.ApplyManualPatchAsync(session.Id, "h1 { color: red; }");
        await _service.ApplyManualPatchAsync(session.Id, "p { color: blue; }");

        var loaded = await _service.GetSessionAsync(session.Id);
        var active = Assert.Single(loaded!.Iterations, i => i.IsActive);
        var html = await _fileStore.ReadTextAsync(active.HtmlFile);
        var patchCss = DesignPatch.ExtractExistingCss(html);
        Assert.NotNull(patchCss);
        Assert.Contains("color: red", patchCss);
        Assert.Contains("color: blue", patchCss);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "mocksmith-patch"));
    }

    [Fact]
    public async Task SaveAsSample_FromSeededSession_CreatesSampleWithMetadata()
    {
        var original = await _importService.ImportAsync(
            "Origin", "sum", ["alpha"],
            "<!doctype html><html><head></head><body>content</body></html>",
            description: "brief", model: "claude-sonnet-5");
        var session = await _service.StartSessionFromSampleAsync(original.Id);
        var iteration = (await _service.GetSessionAsync(session.Id))!.Iterations.Single();

        var saved = await _service.SaveAsSampleAsync(session.Id, iteration.Id, "Saved Copy", "new summary", ["alpha", "beta"]);

        await using var db = _factory.CreateDbContext();
        var persisted = await db.Samples
            .Include(s => s.SampleTags).ThenInclude(st => st.Tag)
            .SingleAsync(s => s.Id == saved.Id);
        Assert.Equal("Saved Copy", persisted.Name);
        Assert.Equal("brief", persisted.Description);
        Assert.Equal("claude-sonnet-5", persisted.Model);
        Assert.Equal(2, persisted.SampleTags.Count);

        var closedSession = await db.DraftSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(DraftSessionStatus.Saved, closedSession.Status);
    }

    [Fact]
    public async Task SaveAsVariant_SameNameTwice_UpsertsSingleVariantWithLatestContent()
    {
        var sample = await _importService.ImportAsync(
            "Base", "", [],
            "<!doctype html><html><head></head><body>v0</body></html>");
        var session = await _service.StartSessionFromSampleAsync(sample.Id);
        var first = (await _service.GetSessionAsync(session.Id))!.Iterations.Single();

        await _service.SaveAsVariantAsync(session.Id, first.Id, "Dark");

        // Second session, same variant name — must overwrite, not duplicate.
        var session2 = await _service.StartSessionFromSampleAsync(sample.Id);
        var iteration2 = (await _service.GetSessionAsync(session2.Id))!.Iterations.Single();
        await _service.ApplyManualPatchAsync(session2.Id, "h1 { color: red; }");
        var patched = (await _service.GetSessionAsync(session2.Id))!.Iterations.Single(i => i.IsActive);
        await _service.SaveAsVariantAsync(session2.Id, patched.Id, "  Dark  ");

        await using var db = _factory.CreateDbContext();
        var variant = Assert.Single(db.Variants.Where(v => v.SampleId == sample.Id));
        Assert.Equal("Dark", variant.Name);
        var html = await _fileStore.ReadTextAsync(variant.HtmlFile);
        Assert.Contains("color: red", html);
        Assert.Equal("h1 { color: red; }", variant.PatchJson);
    }

    [Fact]
    public async Task SaveAsVariant_DifferentNames_CreateSiblings()
    {
        var sample = await _importService.ImportAsync(
            "Base", "", [],
            "<!doctype html><html><head></head><body>v0</body></html>");
        var session = await _service.StartSessionFromSampleAsync(sample.Id);
        var iteration = (await _service.GetSessionAsync(session.Id))!.Iterations.Single();

        await _service.SaveAsVariantAsync(session.Id, iteration.Id, "Dark");
        await _service.SaveAsVariantAsync(session.Id, iteration.Id, "Compact");

        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, db.Variants.Count(v => v.SampleId == sample.Id));
    }

    [Fact]
    public async Task OverwriteOrigin_UpdatesSampleInPlace()
    {
        var sample = await _importService.ImportAsync(
            "Original", "old summary", ["alpha"],
            "<!doctype html><html><head></head><body>OLD</body></html>");
        var session = await _service.StartSessionFromSampleAsync(sample.Id);
        var iteration = (await _service.GetSessionAsync(session.Id))!.Iterations.Single();
        await _service.ApplyManualPatchAsync(session.Id, "body { color: red; }");
        var patched = (await _service.GetSessionAsync(session.Id))!.Iterations.Single(i => i.IsActive);

        await _service.OverwriteOriginAsync(session.Id, patched.Id, "Renamed", "new summary", ["beta"]);

        await using var db = _factory.CreateDbContext();
        var persisted = await db.Samples
            .Include(s => s.SampleTags).ThenInclude(st => st.Tag)
            .SingleAsync(s => s.Id == sample.Id);
        Assert.Equal("Renamed", persisted.Name);
        Assert.Equal("new summary", persisted.Summary);
        Assert.Equal("beta", Assert.Single(persisted.SampleTags).Tag!.Name);
        Assert.Equal(1, await db.Samples.CountAsync());
        var html = await _fileStore.ReadTextAsync(persisted.HtmlFile);
        Assert.Contains("color: red", html);
    }

    [Fact]
    public async Task StartSessionFromVariant_SeedsVariantContentAndName()
    {
        var sample = await _importService.ImportAsync(
            "Base", "", [],
            "<!doctype html><html><head></head><body>base</body></html>");
        var session = await _service.StartSessionFromSampleAsync(sample.Id);
        var iteration = (await _service.GetSessionAsync(session.Id))!.Iterations.Single();
        await _service.ApplyManualPatchAsync(session.Id, "h1 { color: teal; }");
        var patched = (await _service.GetSessionAsync(session.Id))!.Iterations.Single(i => i.IsActive);
        var variant = await _service.SaveAsVariantAsync(session.Id, patched.Id, "Teal");

        var variantSession = await _service.StartSessionFromVariantAsync(variant.Id);
        var loaded = await _service.GetSessionAsync(variantSession.Id);

        Assert.Equal(sample.Id, loaded!.SourceSampleId);
        var seeded = Assert.Single(loaded.Iterations);
        Assert.Equal("Teal", seeded.Name);
        Assert.Contains("color: teal", await _fileStore.ReadTextAsync(seeded.HtmlFile));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_tempRoot, recursive: true);
    }
}
