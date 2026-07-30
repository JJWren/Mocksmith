namespace Mocksmith.Core.Generation;

/// <summary>USD-per-million-token rates for cost estimates on API-backed runs (standard rates, 2026-07).</summary>
public static class ModelPricing
{
    public static readonly IReadOnlyList<string> SupportedModels =
    [
        "claude-sonnet-5",
        "claude-opus-4-8",
        "claude-haiku-4-5",
    ];

    private static readonly Dictionary<string, (decimal InputPerM, decimal OutputPerM)> Rates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-sonnet-5"] = (3m, 15m),
            ["claude-opus-4-8"] = (5m, 25m),
            ["claude-haiku-4-5"] = (1m, 5m),
        };

    public static decimal? Estimate(string model, int inputTokens, int outputTokens)
        => Rates.TryGetValue(model, out var rate)
            ? (inputTokens * rate.InputPerM + outputTokens * rate.OutputPerM) / 1_000_000m
            : null;
}
