using System.Text;

namespace Mocksmith.Core.Services;

/// <summary>Normalizes free-form tag input to the catalog's kebab-case vocabulary.</summary>
public static class TagNormalizer
{
    /// <summary>Lowercase kebab-case: letters/digits kept, all other runs collapse to single dashes.</summary>
    public static string Normalize(string input)
    {
        var builder = new StringBuilder(input.Length);
        var pendingDash = false;
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingDash = false;
                builder.Append(ch);
            }
            else
            {
                pendingDash = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>Normalizes a set of raw tags, dropping empties and duplicates, preserving first-seen order.</summary>
    public static List<string> NormalizeSet(IEnumerable<string> inputs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var input in inputs)
        {
            var normalized = Normalize(input);
            if (normalized.Length > 0 && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }
}
