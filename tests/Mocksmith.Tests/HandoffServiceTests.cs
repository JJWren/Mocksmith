using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Entities;
using Mocksmith.Core.Generation;
using Mocksmith.Core.Services;

namespace Mocksmith.Tests;

public class HandoffServiceTests : IDisposable
{
    private const string SampleHtml =
        """
        <!doctype html><html><head><style>
        :root { --color-bg: #16161a; --font-heading: Georgia, serif; }
        </style></head><body><h1>Steam</h1>
        <script type="application/json" id="mocksmith-tokens">
        {"tokens":[{"name":"--color-bg","label":"Background","category":"color"}]}
        </script>
        </body></html>
        """;

    private readonly SqliteContextFactory _factory = new();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mocksmith-handoff-tests-{Guid.NewGuid():N}");
    private readonly SampleFileStore _fileStore;
    private readonly SampleImportService _importService;
    private readonly StubBriefGenerator _generator = new();
    private readonly HandoffService _service;

    private sealed class StubBriefGenerator : IDesignGenerator
    {
        public string BackendName => "stub";
        public int BriefCalls;

        public Task<DesignGenerationResult> GenerateAsync(
            DesignGenerationRequest request,
            IProgress<GenerationProgress>? progress = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Not used in these tests.");

        public Task<BriefResult> GenerateBriefAsync(BriefRequest request, CancellationToken ct = default)
        {
            BriefCalls++;
            return Task.FromResult(new BriefResult($"# AI brief for {request.Name}", 100, 200, null, 1234));
        }
    }

    public HandoffServiceTests()
    {
        Directory.CreateDirectory(_tempRoot);
        _fileStore = new SampleFileStore(new MocksmithDataOptions { RootPath = _tempRoot });
        _importService = new SampleImportService(_factory, _fileStore, TimeProvider.System);
        _service = new HandoffService(_factory, _fileStore, _generator, TimeProvider.System);
    }

    private Task<Sample> ImportSampleAsync()
        => _importService.ImportAsync(
            "Steam Coffee", "A warm landing page", ["warm", "minimal"], SampleHtml,
            sourceUrl: "https://example.com/inspo", description: "coffee cart landing", model: "claude-sonnet-5");

    [Fact]
    public async Task Bundle_ContainsAllArtifactsWithCorrectContent()
    {
        var sample = await ImportSampleAsync();

        var (content, fileName) = await _service.BuildBundleAsync(sample.Id);

        Assert.Equal("steam-coffee-handoff.zip", fileName);
        using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("sample.html", names);
        Assert.Contains("design-tokens.json", names);
        Assert.Contains("design-brief.md", names);
        Assert.Contains("metadata.json", names);

        Assert.Contains("<h1>Steam</h1>", ReadEntry(zip, "sample.html"));

        using var tokens = JsonDocument.Parse(ReadEntry(zip, "design-tokens.json"));
        Assert.Equal("--color-bg", tokens.RootElement.GetProperty("tokens")[0].GetProperty("name").GetString());

        var brief = ReadEntry(zip, "design-brief.md");
        Assert.Contains("Steam Coffee", brief);
        Assert.Contains("--color-bg", brief);
        Assert.Contains("#16161a", brief);
        Assert.Contains("warm", brief);
        Assert.Contains("https://example.com/inspo", brief);

        using var metadata = JsonDocument.Parse(ReadEntry(zip, "metadata.json"));
        Assert.Equal("Steam Coffee", metadata.RootElement.GetProperty("name").GetString());
        Assert.Equal(2, metadata.RootElement.GetProperty("tags").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, metadata.RootElement.GetProperty("variant").ValueKind);
    }

    [Fact]
    public async Task Bundle_ForVariant_UsesVariantHtmlAndRecordsIt()
    {
        var sample = await ImportSampleAsync();
        var variant = new Variant
        {
            Id = Guid.NewGuid(),
            SampleId = sample.Id,
            Name = "Dark",
            HtmlFile = "",
            PatchJson = ":root { --color-bg: #000; }",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        variant.HtmlFile = _fileStore.VariantHtmlRelativePath(sample.Id, variant.Id);
        await _fileStore.WriteTextAsync(variant.HtmlFile, SampleHtml.Replace("Steam", "Dark Steam"));
        await using (var db = _factory.CreateDbContext())
        {
            db.Variants.Add(variant);
            await db.SaveChangesAsync();
        }

        var (content, fileName) = await _service.BuildBundleAsync(sample.Id, variant.Id);

        Assert.Equal("steam-coffee--dark-handoff.zip", fileName);
        using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        Assert.Contains("Dark Steam", ReadEntry(zip, "sample.html"));
        using var metadata = JsonDocument.Parse(ReadEntry(zip, "metadata.json"));
        Assert.Equal("Dark", metadata.RootElement.GetProperty("variant").GetProperty("name").GetString());
    }

    [Fact]
    public async Task VariantBundle_IgnoresCachedSampleBrief_AndUsesVariantAwareTemplate()
    {
        var sample = await ImportSampleAsync();
        await _service.RegenerateBriefAsync(sample.Id); // caches the base-sample AI brief
        var variant = new Variant
        {
            Id = Guid.NewGuid(),
            SampleId = sample.Id,
            Name = "Dark",
            HtmlFile = "",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        variant.HtmlFile = _fileStore.VariantHtmlRelativePath(sample.Id, variant.Id);
        await _fileStore.WriteTextAsync(variant.HtmlFile, SampleHtml.Replace("#16161a", "#000000"));
        await using (var db = _factory.CreateDbContext())
        {
            db.Variants.Add(variant);
            await db.SaveChangesAsync();
        }

        var (content, _) = await _service.BuildBundleAsync(sample.Id, variant.Id);

        using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var brief = ReadEntry(zip, "design-brief.md");
        Assert.DoesNotContain("AI brief", brief);
        Assert.Contains("Dark variant", brief);
        Assert.Contains("#000000", brief);
    }

    [Fact]
    public async Task AgentPrompt_EmbedsBriefTokensAndHtml()
    {
        var sample = await ImportSampleAsync();

        var prompt = await _service.BuildAgentPromptAsync(sample.Id);

        Assert.Contains("Design handoff", prompt);
        Assert.Contains("```json", prompt);
        Assert.Contains("--color-bg", prompt);
        Assert.Contains("```html", prompt);
        Assert.Contains("<h1>Steam</h1>", prompt);
    }

    [Fact]
    public async Task RegenerateBrief_CachesMarkdownAndLogsCall()
    {
        var sample = await ImportSampleAsync();

        var markdown = await _service.RegenerateBriefAsync(sample.Id);

        Assert.Equal("# AI brief for Steam Coffee", markdown);
        Assert.Equal(1, _generator.BriefCalls);
        await using var db = _factory.CreateDbContext();
        var persisted = await db.Samples.SingleAsync(s => s.Id == sample.Id);
        Assert.Equal(markdown, persisted.BriefMarkdown);
        var log = Assert.Single(db.GenerationLogs);
        Assert.Equal("stub", log.Backend);
        Assert.Equal(200, log.OutputTokens);

        // Subsequent bundles use the cached AI brief instead of the template.
        var (content, _) = await _service.BuildBundleAsync(sample.Id);
        using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        Assert.Equal(markdown, ReadEntry(zip, "design-brief.md"));
    }

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_tempRoot, recursive: true);
    }
}
