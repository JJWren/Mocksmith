using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace Mocksmith.Core.Generation;

/// <summary>Generation via the Anthropic Messages API (official SDK), streaming, with structured output.</summary>
public class ClaudeApiGenerator(string apiKey) : IDesignGenerator
{
    public string BackendName => "api";

    public async Task<BriefResult> GenerateBriefAsync(BriefRequest request, CancellationToken ct = default)
    {
        AnthropicClient client = new() { ApiKey = apiKey };
        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = 4000,
            Messages = [new() { Role = Role.User, Content = DesignPromptBuilder.BuildBriefPrompt(request) }],
        });
        stopwatch.Stop();

        var text = new StringBuilder();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                text.Append(textBlock.Text);
            }
        }

        var inputTokens = (int)response.Usage.InputTokens;
        var outputTokens = (int)response.Usage.OutputTokens;
        return new BriefResult(
            text.ToString().Trim(),
            inputTokens,
            outputTokens,
            ModelPricing.Estimate(request.Model, inputTokens, outputTokens),
            stopwatch.ElapsedMilliseconds);
    }

    public async Task<DesignGenerationResult> GenerateAsync(
        DesignGenerationRequest request,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken ct = default)
    {
        AnthropicClient client = new() { ApiKey = apiKey };

        List<ContentBlockParam> content = [];
        foreach (var shot in request.Screenshots)
        {
            content.Add(new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    Data = Convert.ToBase64String(shot.Data),
                    MediaType = shot.MediaType,
                },
            });
        }

        content.Add(new TextBlockParam { Text = DesignPromptBuilder.BuildUserText(request) });

        using var schemaDoc = JsonDocument.Parse(DesignPromptBuilder.ResultSchemaJson);
        var schema = schemaDoc.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        // Sonnet 5 runs adaptive thinking by default; Opus 4.8 requires the explicit opt-in.
        // Haiku 4.5 does not support adaptive thinking, so it is left unset there.
        ThinkingConfigParam? thinking = request.Model.Contains("haiku", StringComparison.OrdinalIgnoreCase)
            ? null
            : new ThinkingConfigAdaptive();

        List<ToolUnion>? tools = null;
        if (!request.IsRefine && !string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            tools = [new ToolUnion(new WebFetchTool20260209 { MaxUses = 3 })];
        }

        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = 64000,
            System = DesignPromptBuilder.SystemPrompt,
            Messages = [new() { Role = Role.User, Content = content }],
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
            Thinking = thinking,
            Tools = tools,
        };

        var stopwatch = Stopwatch.StartNew();
        var text = new StringBuilder();
        long inputTokens = 0;
        long outputTokens = 0;

        progress?.Report(new GenerationProgress("contacting-api"));
        await foreach (var streamEvent in client.Messages.CreateStreaming(parameters).WithCancellation(ct))
        {
            if (streamEvent.TryPickStart(out var start))
            {
                inputTokens = start.Message.Usage.InputTokens;
                progress?.Report(new GenerationProgress("generating"));
            }
            else if (streamEvent.TryPickContentBlockDelta(out var delta)
                && delta.Delta.TryPickText(out var textDelta))
            {
                text.Append(textDelta.Text);
                if (text.Length % 2000 < textDelta.Text.Length)
                {
                    progress?.Report(new GenerationProgress("generating", text.Length / 4));
                }
            }
            else if (streamEvent.TryPickDelta(out var messageDelta))
            {
                outputTokens = messageDelta.Usage.OutputTokens;
            }
        }

        stopwatch.Stop();
        var payload = GenerationPayload.Parse(text.ToString());

        return new DesignGenerationResult
        {
            Name = payload.Name,
            Summary = payload.Summary,
            Tags = payload.Tags,
            Html = payload.Html,
            Model = request.Model,
            InputTokens = (int)inputTokens,
            OutputTokens = (int)outputTokens,
            EstimatedCostUsd = ModelPricing.Estimate(request.Model, (int)inputTokens, (int)outputTokens),
            DurationMs = stopwatch.ElapsedMilliseconds,
        };
    }
}
