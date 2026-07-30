using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mocksmith.Core.Services;

namespace Mocksmith.Core.Generation;

/// <summary>
/// Generation via the Claude Code CLI in headless mode (subscription-backed).
/// The prompt is piped over stdin; screenshots are written to a temp folder the
/// agent reads with its Read tool; the result arrives as a single JSON envelope.
/// </summary>
public class ClaudeCodeCliGenerator(MocksmithDataOptions dataOptions, string? cliOverride = null, int timeoutSeconds = 600) : IDesignGenerator
{
    public string BackendName => "claude-code";

    public async Task<DesignGenerationResult> GenerateAsync(
        DesignGenerationRequest request,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var workDir = Path.Combine(Path.GetFullPath(dataOptions.RootPath), "tmp", $"cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var prompt = new StringBuilder();
            prompt.AppendLine(DesignPromptBuilder.SystemPrompt);
            prompt.AppendLine();

            if (request.Screenshots.Count > 0)
            {
                var index = 0;
                foreach (var shot in request.Screenshots)
                {
                    var extension = shot.MediaType.Split('/') is [_, var subtype] ? subtype : "png";
                    var path = Path.Combine(workDir, $"screenshot-{index++}.{extension}");
                    await File.WriteAllBytesAsync(path, shot.Data, ct);
                    prompt.AppendLine($"Reference screenshot (read it with the Read tool): {path}");
                }

                prompt.AppendLine();
            }

            prompt.Append(DesignPromptBuilder.BuildUserText(request));

            var psi = BuildStartInfo(request, workDir);
            var stopwatch = Stopwatch.StartNew();
            progress?.Report(new GenerationProgress("starting-claude-cli"));

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start the claude CLI process.");

            await process.StandardInput.WriteAsync(prompt.ToString());
            process.StandardInput.Close();
            progress?.Report(new GenerationProgress("generating"));

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                throw new TimeoutException($"claude CLI did not finish within {timeoutSeconds}s.");
            }

            stopwatch.Stop();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var envelope = ParseEnvelope(stdout);
            if (envelope.IsError || !string.Equals(envelope.Subtype, "success", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"claude CLI reported failure ({envelope.Subtype}): {Truncate(envelope.Result ?? stderr, 500)}");
            }

            var payload = GenerationPayload.Parse(envelope.Result
                ?? throw new InvalidOperationException("claude CLI returned no result text."));

            return new DesignGenerationResult
            {
                Name = payload.Name,
                Summary = payload.Summary,
                Tags = payload.Tags,
                Html = payload.Html,
                Model = request.Model,
                InputTokens = envelope.Usage?.InputTokens ?? 0,
                OutputTokens = envelope.Usage?.OutputTokens ?? 0,
                EstimatedCostUsd = null, // subscription-backed: no marginal dollar cost
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private ProcessStartInfo BuildStartInfo(DesignGenerationRequest request, string workDir)
    {
        var allowedTools = string.IsNullOrWhiteSpace(request.SourceUrl) ? "Read" : "Read WebFetch";
        var cliArguments = new List<string>
        {
            "-p",
            "--output-format", "json",
            "--model", request.Model,
            "--allowed-tools", allowedTools,
            "--disallowed-tools", "Bash Write Edit",
        };

        ProcessStartInfo psi;
        var cli = cliOverride;
        if (cli is null && OperatingSystem.IsWindows())
        {
            // npm global installs expose claude via a .cmd shim, which CreateProcess
            // cannot launch directly; cmd /c resolves it the way a shell would.
            psi = new ProcessStartInfo("cmd.exe");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("claude");
        }
        else
        {
            psi = new ProcessStartInfo(cli ?? "claude");
        }

        foreach (var argument in cliArguments)
        {
            psi.ArgumentList.Add(argument);
        }

        psi.WorkingDirectory = workDir;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;

        // Force subscription auth: with an API key in the environment the CLI would
        // silently bill the key instead.
        psi.Environment.Remove("ANTHROPIC_API_KEY");
        return psi;
    }

    internal record CliUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);

    internal record CliResultEnvelope(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("subtype")] string? Subtype,
        [property: JsonPropertyName("is_error")] bool IsError,
        [property: JsonPropertyName("result")] string? Result,
        [property: JsonPropertyName("usage")] CliUsage? Usage,
        [property: JsonPropertyName("total_cost_usd")] decimal? TotalCostUsd);

    internal static CliResultEnvelope ParseEnvelope(string stdout)
    {
        var start = stdout.IndexOf('{');
        if (start < 0)
        {
            throw new FormatException($"claude CLI produced no JSON envelope: {Truncate(stdout, 300)}");
        }

        return JsonSerializer.Deserialize<CliResultEnvelope>(stdout[start..])
            ?? throw new FormatException("claude CLI envelope deserialized to null.");
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
