using System.Text;
using System.Text.RegularExpressions;

namespace Mocksmith.Core.Generation;

/// <summary>
/// A rule-level edit set from the direct-edit panel: token overrides applied on :root
/// plus per-selector property overrides. Rendered as a single override stylesheet and
/// baked into the sample as an upserted <c>&lt;style id="mocksmith-patch"&gt;</c> block,
/// so re-baking is idempotent.
/// </summary>
public partial class DesignPatch
{
    public Dictionary<string, string> Tokens { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Dictionary<string, string>> Rules { get; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Tokens.Count == 0 && Rules.Count == 0;

    [GeneratedRegex(@"^--[A-Za-z0-9\-_]+$")]
    private static partial Regex TokenNameRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z\-]*$")]
    private static partial Regex PropertyNameRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_.#:\-\s>*\[\]=""']+$")]
    private static partial Regex SelectorRegex();

    [GeneratedRegex("""<style\s+id\s*=\s*["']mocksmith-patch["'][^>]*>(?<css>[\s\S]*?)</style>""", RegexOptions.IgnoreCase)]
    private static partial Regex ExistingPatchRegex();

    /// <summary>Returns the css of an already-baked patch block, or null when absent.</summary>
    public static string? ExtractExistingCss(string html)
    {
        var match = ExistingPatchRegex().Match(html);
        return match.Success ? match.Groups["css"].Value.Trim() : null;
    }

    public void SetToken(string name, string value)
    {
        if (!TokenNameRegex().IsMatch(name))
        {
            throw new ArgumentException($"Invalid token name '{name}'.", nameof(name));
        }

        Tokens[name] = SanitizeValue(value);
    }

    public void SetRule(string selector, string property, string value)
    {
        if (!SelectorRegex().IsMatch(selector))
        {
            throw new ArgumentException($"Invalid selector '{selector}'.", nameof(selector));
        }

        if (!PropertyNameRegex().IsMatch(property))
        {
            throw new ArgumentException($"Invalid CSS property '{property}'.", nameof(property));
        }

        if (!Rules.TryGetValue(selector, out var properties))
        {
            properties = new Dictionary<string, string>(StringComparer.Ordinal);
            Rules[selector] = properties;
        }

        properties[property] = SanitizeValue(value);
    }

    /// <summary>Renders the override stylesheet body (no style tag).</summary>
    public string ToCss()
    {
        var builder = new StringBuilder();
        if (Tokens.Count > 0)
        {
            builder.Append(":root {");
            foreach (var (name, value) in Tokens.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                builder.Append($" {name}: {value};");
            }

            builder.AppendLine(" }");
        }

        foreach (var (selector, properties) in Rules.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            builder.Append($"{selector} {{");
            foreach (var (property, value) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                builder.Append($" {property}: {value};");
            }

            builder.AppendLine(" }");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Upserts the patch stylesheet into a sample document. Replaces an existing
    /// mocksmith-patch block, else inserts before &lt;/head&gt; (fallback: &lt;/html&gt;, else append).
    /// An empty css removes the existing block.
    /// </summary>
    public static string Bake(string html, string css)
    {
        var stripped = ExistingPatchRegex().Replace(html, "");
        if (string.IsNullOrWhiteSpace(css))
        {
            return stripped;
        }

        var block = $"<style id=\"mocksmith-patch\">\n{css}\n</style>";
        var headClose = stripped.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headClose >= 0)
        {
            return stripped.Insert(headClose, block + "\n");
        }

        var htmlClose = stripped.LastIndexOf("</html>", StringComparison.OrdinalIgnoreCase);
        return htmlClose >= 0 ? stripped.Insert(htmlClose, block + "\n") : stripped + "\n" + block;
    }

    /// <summary>
    /// Values are user/UI input destined for a stylesheet inside stored HTML: strip anything
    /// that could close the declaration, the rule, or the style element itself.
    /// </summary>
    private static string SanitizeValue(string value)
        => value.Replace("<", "").Replace(">", "").Replace("{", "").Replace("}", "").Replace(";", "").Trim();
}
