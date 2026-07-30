using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mocksmith.Core.Generation;

/// <summary>The structured {name, summary, tags, html} object both backends return.</summary>
public record GenerationPayload
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("tags")]
    public required List<string> Tags { get; init; }

    [JsonPropertyName("html")]
    public required string Html { get; init; }

    private static readonly JsonSerializerOptions Options = new() { AllowTrailingCommas = true };

    /// <summary>
    /// Parses a payload from model output, tolerating markdown fences or prose around the JSON.
    /// </summary>
    public static GenerationPayload Parse(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new FormatException("Model output contained no JSON object.");
        }

        var json = text[start..(end + 1)];
        return JsonSerializer.Deserialize<GenerationPayload>(json, Options)
            ?? throw new FormatException("Model output JSON deserialized to null.");
    }
}
