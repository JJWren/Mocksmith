namespace Mocksmith.Core.Generation;

/// <summary>Resolves which generation backend to use from config and available credentials.</summary>
public static class GeneratorSelector
{
    public const string Api = "api";
    public const string ClaudeCode = "claude-code";

    /// <summary>
    /// Explicit MOCKSMITH_GENERATOR wins (unknown values disable generation rather than
    /// half-configuring it); otherwise an API key selects "api", then an OAuth token or an
    /// installed CLI selects "claude-code"; null = disabled.
    /// </summary>
    public static string? Resolve(string? explicitMode, bool hasApiKey, bool hasOauthToken, bool cliOnPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitMode))
        {
            var normalized = explicitMode.Trim().ToLowerInvariant();
            return normalized is Api or ClaudeCode ? normalized : null;
        }

        if (hasApiKey)
        {
            return Api;
        }

        if (hasOauthToken || cliOnPath)
        {
            return ClaudeCode;
        }

        return null;
    }

    public static bool IsCliOnPath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        string[] names = OperatingSystem.IsWindows()
            ? ["claude.exe", "claude.cmd", "claude.bat"]
            : ["claude"];
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    if (File.Exists(Path.Combine(directory.Trim(), name)))
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                }
            }
        }

        return false;
    }
}
