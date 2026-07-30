using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mocksmith.Core.Generation;

public record ContractViolation(string Code, string Message);

/// <summary>
/// Checks a generated sample against the single-file token contract:
/// :root custom properties, the mocksmith-tokens manifest, and no external resource requests.
/// </summary>
public static partial class TokenContractValidator
{
    [GeneratedRegex("""<script[^>]*id\s*=\s*["']mocksmith-tokens["'][^>]*>(?<json>[\s\S]*?)</script>""", RegexOptions.IgnoreCase)]
    private static partial Regex ManifestRegex();

    [GeneratedRegex("""<(?:link|script|img|iframe|source|video|audio|embed|object)\b[^>]*?(?:src|href|data)\s*=\s*["'](?:https?:)?//""", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalTagRegex();

    [GeneratedRegex("""(?:url\(\s*["']?|@import\s+["']?)(?:https?:)?//""", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalCssRegex();

    // JS-initiated requests matter beyond the app's CSP: exported/handled-off samples
    // run standalone, where fetch/XHR/WebSocket calls would actually fire.
    [GeneratedRegex(
        """(?:fetch\s*\(\s*["'`](?:https?:)?//|new\s+WebSocket\s*\(\s*["'`]wss?://|new\s+EventSource\s*\(\s*["'`](?:https?:)?//|\.open\s*\(\s*["'`][A-Za-z]+["'`]\s*,\s*["'`](?:https?:)?//|sendBeacon\s*\(\s*["'`](?:https?:)?//)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex ExternalJsRegex();

    public static IReadOnlyList<ContractViolation> Validate(string html)
    {
        var violations = new List<ContractViolation>();

        if (!html.Contains(":root", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new ContractViolation("missing-root", "No :root block with CSS custom properties."));
        }
        else
        {
            if (!html.Contains("--color-", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new ContractViolation("missing-color-tokens", "No --color-* custom properties found."));
            }

            if (!html.Contains("--font-", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new ContractViolation("missing-font-tokens", "No --font-* custom properties found."));
            }
        }

        if (TryExtractManifestJson(html) is not { } manifestJson)
        {
            violations.Add(new ContractViolation("missing-manifest", "No <script id=\"mocksmith-tokens\"> manifest block."));
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(manifestJson);
                if (!doc.RootElement.TryGetProperty("tokens", out var tokens)
                    || tokens.ValueKind != JsonValueKind.Array
                    || tokens.GetArrayLength() == 0)
                {
                    violations.Add(new ContractViolation("empty-manifest", "Token manifest has no tokens array entries."));
                }
            }
            catch (JsonException)
            {
                violations.Add(new ContractViolation("invalid-manifest", "Token manifest is not valid JSON."));
            }
        }

        if (ExternalTagRegex().IsMatch(html) || ExternalCssRegex().IsMatch(html) || ExternalJsRegex().IsMatch(html))
        {
            violations.Add(new ContractViolation("external-request", "Page references external resources (tag src/href, CSS url()/@import, or JS fetch/XHR/WebSocket to a network URL)."));
        }

        return violations;
    }

    /// <summary>Returns the raw manifest JSON, or null when the block is absent.</summary>
    public static string? TryExtractManifestJson(string html)
    {
        var match = ManifestRegex().Match(html);
        return match.Success ? match.Groups["json"].Value.Trim() : null;
    }
}
