namespace Mocksmith.Core.Services;

/// <summary>Reads and writes sample/asset files under the data root using relative paths.</summary>
public class SampleFileStore(MocksmithDataOptions options)
{
    // Windows filesystems are case-insensitive; a case-sensitive prefix check
    // would reject legitimate paths whose drive/root casing differs.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _root = Path.GetFullPath(options.RootPath);

    public string SampleHtmlRelativePath(Guid sampleId) => $"samples/{sampleId}/sample.html";

    public string SessionIterationRelativePath(Guid sessionId, int index) => $"sessions/{sessionId}/iter-{index}.html";

    public string AssetRelativePath(Guid assetId, string extension) => $"assets/{assetId}.{extension.TrimStart('.')}";

    public async Task WriteTextAsync(string relativePath, string text, CancellationToken ct = default)
    {
        var absolute = ResolveAbsolute(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        await File.WriteAllTextAsync(absolute, text, ct);
    }

    public async Task WriteBytesAsync(string relativePath, byte[] data, CancellationToken ct = default)
    {
        var absolute = ResolveAbsolute(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        await File.WriteAllBytesAsync(absolute, data, ct);
    }

    public Task<byte[]> ReadBytesAsync(string relativePath, CancellationToken ct = default)
        => File.ReadAllBytesAsync(ResolveAbsolute(relativePath), ct);

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
        if (!combined.StartsWith(_root + Path.DirectorySeparatorChar, PathComparison)
            && !combined.Equals(_root, PathComparison))
        {
            throw new InvalidOperationException($"Path '{relativePath}' escapes the data root.");
        }

        return combined;
    }

    /// <summary>Best-effort delete for compensating cleanup; never throws for IO races.</summary>
    public void TryDelete(string relativePath)
    {
        try
        {
            var absolute = ResolveAbsolute(relativePath);
            File.Delete(absolute);
            var directory = Path.GetDirectoryName(absolute);
            if (directory is not null
                && !directory.Equals(_root, PathComparison)
                && Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
