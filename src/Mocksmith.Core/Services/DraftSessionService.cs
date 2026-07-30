using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mocksmith.Core.Data;
using Mocksmith.Core.Entities;
using Mocksmith.Core.Generation;

namespace Mocksmith.Core.Services;

/// <summary>Which generation backend is active; Backend is null when generation is unconfigured.</summary>
public record GenerationOptions
{
    public string? Backend { get; init; }
}

/// <summary>
/// Orchestrates draft sessions: fan-out candidate generation, contract validation with
/// one automatic repair round-trip, refine turns, and promotion to a saved sample.
/// </summary>
public class DraftSessionService(
    IDbContextFactory<MocksmithDbContext> contextFactory,
    IDesignGenerator generator,
    SampleFileStore fileStore,
    SampleImportService importService,
    TimeProvider timeProvider)
{
    public async Task<DraftSession> StartSessionAsync(
        string description,
        string? sourceUrl,
        string model,
        IReadOnlyList<ScreenshotInput> screenshots,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("A design description is required.", nameof(description));
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var session = new DraftSession
        {
            Id = Guid.NewGuid(),
            Status = DraftSessionStatus.Active,
            CreatedAt = now,
            Description = description.Trim(),
            SourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl.Trim(),
            Model = model,
        };

        foreach (var shot in screenshots)
        {
            var asset = new InputAsset
            {
                Id = Guid.NewGuid(),
                DraftSession = session,
                FileName = shot.FileName,
                ContentType = shot.MediaType,
                FilePath = "",
                CreatedAt = now,
            };
            asset.FilePath = fileStore.AssetRelativePath(asset.Id, ExtensionFor(shot.MediaType));
            await fileStore.WriteBytesAsync(asset.FilePath, shot.Data, ct);
            session.Assets.Add(asset);
        }

        db.DraftSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>
    /// Opens the workspace on a saved sample: a new session seeded with the sample's
    /// HTML as iteration 0, carrying its metadata so refine/panel/save flows work unchanged.
    /// </summary>
    public async Task<DraftSession> StartSessionFromSampleAsync(Guid sampleId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var sample = await db.Samples
            .AsNoTracking()
            .Include(s => s.SampleTags).ThenInclude(st => st.Tag)
            .FirstOrDefaultAsync(s => s.Id == sampleId, ct)
            ?? throw new InvalidOperationException($"Sample {sampleId} not found.");

        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var session = new DraftSession
        {
            Id = Guid.NewGuid(),
            Status = DraftSessionStatus.Active,
            CreatedAt = now,
            Description = string.IsNullOrWhiteSpace(sample.Description) ? sample.Name : sample.Description,
            SourceUrl = sample.SourceUrl,
            Model = sample.Model ?? settings.DefaultModel,
            SourceSampleId = sample.Id,
        };

        var html = await fileStore.ReadTextAsync(sample.HtmlFile, ct);
        var relativePath = fileStore.SessionIterationRelativePath(session.Id, 0);
        await fileStore.WriteTextAsync(relativePath, html, ct);
        session.Iterations.Add(new DraftIteration
        {
            Id = Guid.NewGuid(),
            DraftSession = session,
            Index = 0,
            CandidateGroup = 0,
            InstructionText = null,
            HtmlFile = relativePath,
            Model = session.Model,
            IsActive = true,
            CreatedAt = now,
            Name = sample.Name,
            Summary = sample.Summary,
            TagsJson = JsonSerializer.Serialize(sample.SampleTags.Select(st => st.Tag!.Name).ToList()),
        });

        db.DraftSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>
    /// Opens the workspace on a variant: like <see cref="StartSessionFromSampleAsync"/> but
    /// seeded with the variant's HTML and name, so save-as-variant prefills to an upsert.
    /// </summary>
    public async Task<DraftSession> StartSessionFromVariantAsync(Guid variantId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var variant = await db.Variants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId, ct)
            ?? throw new InvalidOperationException($"Variant {variantId} not found.");

        var session = await StartSessionFromSampleAsync(variant.SampleId, ct);

        var html = await fileStore.ReadTextAsync(variant.HtmlFile, ct);
        await using var updateDb = await contextFactory.CreateDbContextAsync(ct);
        var iteration = await updateDb.DraftIterations.FirstAsync(i => i.DraftSessionId == session.Id, ct);
        await fileStore.WriteTextAsync(iteration.HtmlFile, html, ct);
        iteration.Name = variant.Name;
        await updateDb.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>Overwrites the origin sample in place with an iteration's content and metadata.</summary>
    public async Task<Sample> OverwriteOriginAsync(
        Guid sessionId,
        Guid iterationId,
        string name,
        string summary,
        IEnumerable<string> tags,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A sample name is required.", nameof(name));
        }

        var session = await GetSessionAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");
        var originId = session.SourceSampleId
            ?? throw new InvalidOperationException("This session has no origin sample to overwrite.");
        var iteration = session.Iterations.FirstOrDefault(i => i.Id == iterationId)
            ?? throw new InvalidOperationException("Iteration not found in session.");

        var html = await fileStore.ReadTextAsync(iteration.HtmlFile, ct);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var sample = await db.Samples.Include(s => s.SampleTags).FirstOrDefaultAsync(s => s.Id == originId, ct)
            ?? throw new InvalidOperationException("Origin sample no longer exists.");

        await fileStore.WriteTextAsync(sample.HtmlFile, html, ct);
        sample.Name = name.Trim();
        sample.Summary = summary.Trim();
        sample.Model = iteration.Model;
        sample.TokensJson = TokenContractValidator.TryExtractManifestJson(html);
        sample.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        sample.SampleTags.Clear();
        var tagNames = TagNormalizer.NormalizeSet(tags);
        var existing = await db.Tags.Where(t => tagNames.Contains(t.Name)).ToListAsync(ct);
        foreach (var tagName in tagNames)
        {
            var tag = existing.FirstOrDefault(t => t.Name == tagName) ?? new Tag { Name = tagName };
            sample.SampleTags.Add(new SampleTag { SampleId = sample.Id, Tag = tag });
        }

        await db.SaveChangesAsync(ct);

        // Only mark the session saved once the overwrite has actually committed.
        await db.DraftSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, DraftSessionStatus.Saved), ct);
        return sample;
    }

    /// <summary>
    /// Saves an iteration as a named variant of the origin sample. The name is an upsert
    /// key (unique per sample): an existing variant with that name is overwritten in place.
    /// </summary>
    public async Task<Variant> SaveAsVariantAsync(
        Guid sessionId,
        Guid iterationId,
        string variantName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(variantName))
        {
            throw new ArgumentException("A variant name is required.", nameof(variantName));
        }

        var session = await GetSessionAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");
        var originId = session.SourceSampleId
            ?? throw new InvalidOperationException("This session has no origin sample for a variant.");
        var iteration = session.Iterations.FirstOrDefault(i => i.Id == iterationId)
            ?? throw new InvalidOperationException("Iteration not found in session.");

        var html = await fileStore.ReadTextAsync(iteration.HtmlFile, ct);
        var trimmedName = variantName.Trim();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var variant = await db.Variants.FirstOrDefaultAsync(v => v.SampleId == originId && v.Name == trimmedName, ct);
        var created = variant is null;
        if (variant is null)
        {
            variant = new Variant
            {
                Id = Guid.NewGuid(),
                SampleId = originId,
                Name = trimmedName,
                HtmlFile = "",
                CreatedAt = now,
                UpdatedAt = now,
            };
            variant.HtmlFile = fileStore.VariantHtmlRelativePath(originId, variant.Id);
            db.Variants.Add(variant);
        }
        else
        {
            variant.UpdatedAt = now;
        }

        variant.PatchJson = DesignPatch.ExtractExistingCss(html);
        await fileStore.WriteTextAsync(variant.HtmlFile, html, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch when (created)
        {
            // Compensate the file write for a variant row that never committed.
            fileStore.TryDelete(variant.HtmlFile);
            throw;
        }
        await db.DraftSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, DraftSessionStatus.Saved), ct);
        return variant;
    }

    /// <summary>
    /// Bakes a direct-edit panel patch into the active iteration's HTML as a new
    /// active iteration, keeping the panel's edits inside the normal iteration history.
    /// </summary>
    public async Task ApplyManualPatchAsync(Guid sessionId, string patchCss, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patchCss))
        {
            return;
        }

        var session = await GetSessionAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");
        var active = session.Iterations.FirstOrDefault(i => i.IsActive)
            ?? throw new InvalidOperationException("No active iteration to edit.");

        var html = await fileStore.ReadTextAsync(active.HtmlFile, ct);

        // Concatenate with any previously baked patch (later rules win the cascade)
        // so applies accumulate across separate workspace visits instead of replacing.
        var existing = DesignPatch.ExtractExistingCss(html);
        var combined = string.IsNullOrWhiteSpace(existing) ? patchCss : existing + "\n" + patchCss;
        var baked = DesignPatch.Bake(html, combined);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var nextIndex = await NextIterationIndexAsync(db, sessionId, ct);
        var relativePath = fileStore.SessionIterationRelativePath(sessionId, nextIndex);
        await fileStore.WriteTextAsync(relativePath, baked, ct);
        var iteration = new DraftIteration
        {
            Id = Guid.NewGuid(),
            DraftSessionId = sessionId,
            Index = nextIndex,
            CandidateGroup = active.CandidateGroup,
            InstructionText = "manual edit (panel)",
            HtmlFile = relativePath,
            Model = active.Model,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            Name = active.Name,
            Summary = active.Summary,
            TagsJson = active.TagsJson,
        };
        db.DraftIterations.Add(iteration);
        await db.SaveChangesAsync(ct);
        await db.DraftIterations
            .Where(i => i.DraftSessionId == sessionId && i.Id != iteration.Id && i.IsActive)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.IsActive, false), ct);
    }

    public async Task<DraftSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.DraftSessions
            .AsNoTracking()
            .Include(s => s.Iterations)
            .Include(s => s.Assets)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
    }

    /// <summary>Runs the initial fan-out; candidates share a CandidateGroup.</summary>
    public async Task GenerateCandidatesAsync(
        Guid sessionId,
        int fanOut,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken ct = default)
    {
        fanOut = Math.Clamp(fanOut, 1, 3);
        var session = await GetSessionAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");
        var request = await BuildRequestAsync(session, currentHtml: null, instruction: null, ct);
        var group = session.Iterations.Count == 0 ? 0 : session.Iterations.Max(i => i.CandidateGroup) + 1;

        var results = new List<DesignGenerationResult>();
        var errors = new List<Exception>();
        var tasks = Enumerable.Range(0, fanOut).Select(async candidate =>
        {
            try
            {
                var candidateProgress = progress is null
                    ? null
                    : new Progress<GenerationProgress>(p =>
                        progress.Report(p with { Phase = $"candidate {candidate + 1}/{fanOut}: {p.Phase}" }));
                var result = await RunWithRepairAsync(request, sessionId, candidateProgress, ct);
                lock (results)
                {
                    results.Add(result);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (errors)
                {
                    errors.Add(ex);
                }
            }
        });
        await Task.WhenAll(tasks);

        if (results.Count == 0)
        {
            throw new InvalidOperationException(
                $"All {fanOut} generation attempt(s) failed: {errors.FirstOrDefault()?.Message}",
                errors.FirstOrDefault());
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var nextIndex = await NextIterationIndexAsync(db, sessionId, ct);
        var hasActive = await db.DraftIterations.AnyAsync(i => i.DraftSessionId == sessionId && i.IsActive, ct);
        foreach (var result in results)
        {
            var iteration = await CreateIterationAsync(db, sessionId, nextIndex++, group, null, result, ct);
            if (!hasActive)
            {
                iteration.IsActive = true;
                hasActive = true;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Applies a refine instruction to the active iteration, producing a new active iteration.</summary>
    public async Task RefineAsync(
        Guid sessionId,
        string instruction,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            throw new ArgumentException("A refine instruction is required.", nameof(instruction));
        }

        var session = await GetSessionAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");
        var active = session.Iterations.FirstOrDefault(i => i.IsActive)
            ?? throw new InvalidOperationException("No active iteration to refine.");

        var currentHtml = await fileStore.ReadTextAsync(active.HtmlFile, ct);
        var request = await BuildRequestAsync(session, currentHtml, instruction.Trim(), ct);
        var result = await RunWithRepairAsync(request, sessionId, progress, ct);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var nextIndex = await NextIterationIndexAsync(db, sessionId, ct);
        var iteration = await CreateIterationAsync(db, sessionId, nextIndex, active.CandidateGroup, instruction.Trim(), result, ct);
        iteration.IsActive = true;
        await db.DraftIterations
            .Where(i => i.DraftSessionId == sessionId && i.Id != iteration.Id && i.IsActive)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.IsActive, false), ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(Guid sessionId, Guid iterationId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await db.DraftIterations
            .Where(i => i.DraftSessionId == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.IsActive, i => i.Id == iterationId), ct);
    }

    /// <summary>Promotes an iteration to a saved Sample and closes the session.</summary>
    public async Task<Sample> SaveAsSampleAsync(
        Guid sessionId,
        Guid iterationId,
        string name,
        string summary,
        IEnumerable<string> tags,
        CancellationToken ct = default)
    {
        var session = await GetSessionAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");
        var iteration = session.Iterations.FirstOrDefault(i => i.Id == iterationId)
            ?? throw new InvalidOperationException("Iteration not found in session.");

        var html = await fileStore.ReadTextAsync(iteration.HtmlFile, ct);
        var sample = await importService.ImportAsync(
            name,
            summary,
            tags,
            html,
            sourceUrl: session.SourceUrl,
            description: session.Description,
            model: iteration.Model,
            tokensJson: TokenContractValidator.TryExtractManifestJson(html),
            ct: ct);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await db.InputAssets
            .Where(a => a.DraftSessionId == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.SampleId, sample.Id), ct);
        await db.DraftSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, DraftSessionStatus.Saved), ct);
        return sample;
    }

    private async Task<DesignGenerationRequest> BuildRequestAsync(
        DraftSession session,
        string? currentHtml,
        string? instruction,
        CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var vocabulary = await db.Tags.AsNoTracking().OrderBy(t => t.Name).Select(t => t.Name).ToListAsync(ct);

        var screenshots = new List<ScreenshotInput>();
        if (currentHtml is null)
        {
            foreach (var asset in session.Assets)
            {
                screenshots.Add(new ScreenshotInput(
                    asset.FileName,
                    asset.ContentType,
                    await fileStore.ReadBytesAsync(asset.FilePath, ct)));
            }
        }

        return new DesignGenerationRequest
        {
            Description = session.Description,
            Screenshots = screenshots,
            SourceUrl = session.SourceUrl,
            Model = session.Model,
            ExistingTags = vocabulary,
            CurrentHtml = currentHtml,
            Instruction = instruction,
        };
    }

    /// <summary>Runs one generation; on contract violations, one automatic repair round-trip.</summary>
    private async Task<DesignGenerationResult> RunWithRepairAsync(
        DesignGenerationRequest request,
        Guid sessionId,
        IProgress<GenerationProgress>? progress,
        CancellationToken ct)
    {
        var result = await generator.GenerateAsync(request, progress, ct);
        await LogAsync(result, sessionId, ct);

        var violations = TokenContractValidator.Validate(result.Html);
        if (violations.Count == 0)
        {
            return result;
        }

        progress?.Report(new GenerationProgress("repairing-contract"));
        var repairRequest = request with
        {
            CurrentHtml = result.Html,
            Instruction = "Fix these output-contract violations while preserving the design: "
                + string.Join("; ", violations.Select(v => $"{v.Code}: {v.Message}")),
            Screenshots = [],
        };
        var repaired = await generator.GenerateAsync(repairRequest, progress, ct);
        await LogAsync(repaired, sessionId, ct);

        // Keep the model-proposed metadata from the first pass; the repair pass only fixes HTML.
        return repaired with { Name = result.Name, Summary = result.Summary, Tags = result.Tags };
    }

    private async Task<DraftIteration> CreateIterationAsync(
        MocksmithDbContext db,
        Guid sessionId,
        int index,
        int candidateGroup,
        string? instruction,
        DesignGenerationResult result,
        CancellationToken ct)
    {
        var relativePath = fileStore.SessionIterationRelativePath(sessionId, index);
        await fileStore.WriteTextAsync(relativePath, result.Html, ct);
        var iteration = new DraftIteration
        {
            Id = Guid.NewGuid(),
            DraftSessionId = sessionId,
            Index = index,
            CandidateGroup = candidateGroup,
            InstructionText = instruction,
            HtmlFile = relativePath,
            Model = result.Model,
            IsActive = false,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            Name = result.Name,
            Summary = result.Summary,
            TagsJson = JsonSerializer.Serialize(result.Tags),
        };
        db.DraftIterations.Add(iteration);
        return iteration;
    }

    private async Task LogAsync(DesignGenerationResult result, Guid sessionId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        db.GenerationLogs.Add(new GenerationLog
        {
            Model = result.Model,
            Backend = generator.BackendName,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            EstimatedCostUsd = result.EstimatedCostUsd,
            DurationMs = result.DurationMs,
            DraftSessionId = sessionId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task<int> NextIterationIndexAsync(MocksmithDbContext db, Guid sessionId, CancellationToken ct)
    {
        var maxIndex = await db.DraftIterations
            .Where(i => i.DraftSessionId == sessionId)
            .Select(i => (int?)i.Index)
            .MaxAsync(ct);
        return (maxIndex ?? -1) + 1;
    }

    private static string ExtensionFor(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/webp" => "webp",
        "image/gif" => "gif",
        _ => "bin",
    };
}
