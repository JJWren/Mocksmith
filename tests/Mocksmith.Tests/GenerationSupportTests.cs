using Mocksmith.Core.Generation;

namespace Mocksmith.Tests;

public class GenerationSupportTests
{
    [Fact]
    public void PromptBuilder_InjectsVocabularyAndBrief()
    {
        var request = new DesignGenerationRequest
        {
            Description = "A warm bakery landing page",
            Model = "claude-sonnet-5",
            ExistingTags = ["dark", "dashboard"],
        };

        var text = DesignPromptBuilder.BuildUserText(request);

        Assert.Contains("A warm bakery landing page", text);
        Assert.Contains("dark, dashboard", text);
        Assert.Contains("\"name\"", text);
    }

    [Fact]
    public void PromptBuilder_RefineMode_IncludesInstructionAndCurrentHtml()
    {
        var request = new DesignGenerationRequest
        {
            Description = "ignored in refine",
            Model = "claude-sonnet-5",
            CurrentHtml = "<html>CURRENT</html>",
            Instruction = "make the header darker",
        };

        var text = DesignPromptBuilder.BuildUserText(request);

        Assert.Contains("make the header darker", text);
        Assert.Contains("<html>CURRENT</html>", text);
        Assert.Contains("Refine", text);
    }

    [Theory]
    [InlineData("claude-sonnet-5", 1000, 2000, 0.033)]
    [InlineData("claude-opus-4-8", 1000, 2000, 0.055)]
    [InlineData("claude-haiku-4-5", 1000, 2000, 0.011)]
    public void Pricing_EstimatesKnownModels(string model, int input, int output, decimal expected)
    {
        Assert.Equal(expected, ModelPricing.Estimate(model, input, output));
    }

    [Fact]
    public void Pricing_UnknownModel_ReturnsNull()
    {
        Assert.Null(ModelPricing.Estimate("claude-imaginary-9", 10, 10));
    }

    [Fact]
    public void Payload_Parse_ToleratesFencesAndProse()
    {
        const string text =
            """
            Here you go:
            ```json
            {"name":"Neon","summary":"A neon page","tags":["neon","dark"],"html":"<html>x</html>"}
            ```
            """;

        var payload = GenerationPayload.Parse(text);

        Assert.Equal("Neon", payload.Name);
        Assert.Equal(["neon", "dark"], payload.Tags);
    }

    [Fact]
    public void Payload_Parse_NoJson_Throws()
    {
        Assert.Throws<FormatException>(() => GenerationPayload.Parse("no json here"));
    }

    [Fact]
    public void CliEnvelope_Parses()
    {
        const string stdout =
            """
            {"type":"result","subtype":"success","is_error":false,"result":"{\"name\":\"X\",\"summary\":\"s\",\"tags\":[\"a\"],\"html\":\"<html></html>\"}","usage":{"input_tokens":120,"output_tokens":450},"total_cost_usd":0.0123}
            """;

        var envelope = ClaudeCodeCliGenerator.ParseEnvelope(stdout);

        Assert.False(envelope.IsError);
        Assert.Equal("success", envelope.Subtype);
        Assert.Equal(120, envelope.Usage!.InputTokens);
        var payload = GenerationPayload.Parse(envelope.Result!);
        Assert.Equal("X", payload.Name);
    }

    [Theory]
    [InlineData(null, false, false, false, null)]
    [InlineData(null, true, false, false, "api")]
    [InlineData(null, false, true, false, "claude-code")]
    [InlineData(null, false, false, true, "claude-code")]
    [InlineData(null, true, true, true, "api")]
    [InlineData("claude-code", true, false, false, "claude-code")]
    [InlineData(" API ", false, false, false, "api")]
    [InlineData("bogus-backend", true, true, true, null)]
    public void Selector_ResolvesBackend(string? mode, bool key, bool oauth, bool cli, string? expected)
    {
        Assert.Equal(expected, GeneratorSelector.Resolve(mode, key, oauth, cli));
    }
}
