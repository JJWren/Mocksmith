using Mocksmith.Core.Generation;
using Mocksmith.Core.Services;

namespace Mocksmith.Tests;

/// <summary>
/// Live subscription-backed test of the CLI backend. Guarded by MOCKSMITH_LIVE_CLI_TEST=1
/// so CI (no claude CLI, no credentials) never executes it.
/// </summary>
public class ClaudeCodeCliLiveTests
{
    [Fact]
    public async Task LiveGeneration_ThroughCli_ProducesContractCompliantSample()
    {
        if (Environment.GetEnvironmentVariable("MOCKSMITH_LIVE_CLI_TEST") != "1")
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"mocksmith-live-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var generator = new ClaudeCodeCliGenerator(new MocksmithDataOptions { RootPath = tempRoot });
            var result = await generator.GenerateAsync(new DesignGenerationRequest
            {
                Description = "A tiny, minimal landing page for a coffee cart called Steam. "
                    + "One hero section and one small menu list only — keep it very small.",
                Model = "claude-sonnet-5",
                ExistingTags = ["minimal", "warm"],
            });

            Assert.False(string.IsNullOrWhiteSpace(result.Name));
            Assert.False(string.IsNullOrWhiteSpace(result.Summary));
            Assert.NotEmpty(result.Tags);
            Assert.Contains("<html", result.Html, StringComparison.OrdinalIgnoreCase);
            Assert.Null(result.EstimatedCostUsd);
            Assert.True(result.OutputTokens > 0);

            var violations = TokenContractValidator.Validate(result.Html);
            Assert.True(violations.Count == 0,
                "Contract violations: " + string.Join("; ", violations.Select(v => $"{v.Code}: {v.Message}")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
