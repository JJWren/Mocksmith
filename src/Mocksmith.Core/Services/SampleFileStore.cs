namespace Mocksmith.Core.Services;

/// <summary>Reads and writes sample/asset files under the data root using relative paths.</summary>
public class SampleFileStore(MocksmithDataOptions options)
{
    private readonly string _root = Path.GetFullPath(options.RootPath);

    public string SampleHtmlRelativePath(Guid sampleId) => $"samples/{sampleId}/sample.html";

    public async Task<string> WriteSampleHtmlAsync(Guid sampleId, string html, CancellationToken ct = default)
    {
        var relative = SampleHtmlRelativePath(sampleId);
        var absolute = ResolveAbsolute(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        await File.WriteAllTextAsync(absolute, html, ct);
        return relative;
    }

    public Task<string> ReadTextAsync(string relativePath, CancellationToken ct = default)
        => File.ReadAllTextAsync(ResolveAbsolute(relativePath), ct);

    public bool Exists(string relativePath) => File.Exists(ResolveAbsolute(relativePath));

    /// <summary>Resolves a stored relative path, rejecting anything that escapes the data root.</summary>
    public string ResolveAbsolute(string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!combined.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && combined != _root)
        {
            throw new InvalidOperationException($"Path '{relativePath}' escapes the data root.");
        }

        return combined;
    }
}
