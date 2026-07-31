using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mocksmith.Core.Data;
using Mocksmith.Core.Generation;
using Mocksmith.Core.Services;

namespace Mocksmith.Tests;

public class DraftGenerationCoordinatorTests : IDisposable
{
    // Passes TokenContractValidator so runs complete without a repair round.
    private const string ContractHtml =
        """
        <!doctype html><html><head><style>
        :root { --color-bg: #111; --color-text: #eee; --font-heading: Georgia, serif; --font-body: system-ui; }
        body { background: var(--color-bg); }
        </style></head><body>
        <h1>Hi</h1>
        <script type="application/json" id="mocksmith-tokens">
        {"tokens":[{"name":"--color-bg","label":"Background","category":"color"}]}
        </script>
        </body></html>
        """;

    /// <summary>Generator that blocks until the test releases (or fails) it.</summary>
    private sealed class GateGenerator : IDesignGenerator
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string BackendName => "gate";

        public void Release() => _gate.TrySetResult();

        public void Fail(Exception ex) => _gate.TrySetException(ex);

        public async Task<DesignGenerationResult> GenerateAsync(
            DesignGenerationRequest request,
            IProgress<GenerationProgress>? progress = null,
            CancellationToken ct = default)
        {
            progress?.Report(new GenerationProgress("generating", 42));
            await _gate.Task.WaitAsync(ct);
            return new DesignGenerationResult
            {
                Name = "Stub Candidate",
                Summary = "stubbed",
                Tags = ["stub"],
                Html = ContractHtml,
                Model = request.Model,
                InputTokens = 1,
                OutputTokens = 2,
                EstimatedCostUsd = null,
                DurationMs = 5,
            };
        }

        public Task<BriefResult> GenerateBriefAsync(BriefRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("Not used in these tests.");
    }

    private readonly SqliteContextFactory _factory = new();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mocksmith-coordinator-tests-{Guid.NewGuid():N}");
    private readonly GateGenerator _generator = new();
    private readonly ServiceProvider _provider;
    private readonly DraftGenerationCoordinator _coordinator;
    private readonly DraftSessionService _service;

    public DraftGenerationCoordinatorTests()
    {
        Directory.CreateDirectory(_tempRoot);
        var fileStore = new SampleFileStore(new MocksmithDataOptions { RootPath = _tempRoot });

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<MocksmithDbContext>>(_factory);
        services.AddSingleton(fileStore);
        services.AddSingleton<IDesignGenerator>(_generator);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SampleImportService>();
        services.AddScoped<DraftSessionService>();
        _provider = services.BuildServiceProvider();

        _coordinator = new DraftGenerationCoordinator(
            _provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        _service = new DraftSessionService(
            _factory, _generator, fileStore,
            new SampleImportService(_factory, fileStore, TimeProvider.System), TimeProvider.System);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Condition not reached in time.");
            }

            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task Run_SurvivesSubscriberDetach_AndPersistsCandidates()
    {
        var session = await _service.StartSessionAsync("desc", null, "claude-sonnet-5", []);

        Action<Guid> handler = _ => { };
        _coordinator.Changed += handler;
        Assert.True(_coordinator.TryStartCandidates(session.Id, 1));
        _coordinator.Changed -= handler; // the user navigated away

        await WaitForAsync(() => _coordinator.GetState(session.Id)?.Phase is not null);
        Assert.Contains("candidate 1", _coordinator.GetState(session.Id)!.Phase);

        _generator.Release();
        await WaitForAsync(() => _coordinator.GetState(session.Id) is { Running: false });

        var state = _coordinator.GetState(session.Id)!;
        Assert.Null(state.Error);
        Assert.False(state.Cancelled);

        var loaded = await _service.GetSessionAsync(session.Id);
        var iteration = Assert.Single(loaded!.Iterations);
        Assert.True(iteration.IsActive);
        Assert.Equal("Stub Candidate", iteration.Name);
    }

    [Fact]
    public async Task Cancel_MarksCancelled_AndPersistsNothing()
    {
        var session = await _service.StartSessionAsync("desc", null, "claude-sonnet-5", []);
        Assert.True(_coordinator.TryStartCandidates(session.Id, 1));

        _coordinator.Cancel(session.Id);
        await WaitForAsync(() => _coordinator.GetState(session.Id) is { Running: false });

        var state = _coordinator.GetState(session.Id)!;
        Assert.True(state.Cancelled);
        Assert.Null(state.Error);
        Assert.Empty((await _service.GetSessionAsync(session.Id))!.Iterations);
    }

    [Fact]
    public async Task SecondStart_WhileRunning_IsRejected_ThenAllowedAfterCompletion()
    {
        var session = await _service.StartSessionAsync("desc", null, "claude-sonnet-5", []);
        Assert.True(_coordinator.TryStartCandidates(session.Id, 1));
        Assert.False(_coordinator.TryStartCandidates(session.Id, 1));
        Assert.False(_coordinator.TryStartRefine(session.Id, "darker"));

        _generator.Release();
        await WaitForAsync(() => _coordinator.GetState(session.Id) is { Running: false });

        // Gate already open, so this second run completes immediately.
        Assert.True(_coordinator.TryStartCandidates(session.Id, 1));
        await WaitForAsync(() => _coordinator.GetState(session.Id) is { Running: false });
        Assert.Equal(2, (await _service.GetSessionAsync(session.Id))!.Iterations.Count);
    }

    [Fact]
    public async Task Refine_RunsThroughCoordinator_AndActivatesNewIteration()
    {
        using var scope = _provider.CreateScope();
        var importService = scope.ServiceProvider.GetRequiredService<SampleImportService>();
        var sample = await importService.ImportAsync(
            "Base", "", [], "<!doctype html><html><head></head><body>x</body></html>");
        var session = await _service.StartSessionFromSampleAsync(sample.Id);

        Assert.True(_coordinator.TryStartRefine(session.Id, "darker"));
        _generator.Release();
        await WaitForAsync(() => _coordinator.GetState(session.Id) is { Running: false });

        Assert.Null(_coordinator.GetState(session.Id)!.Error);
        var loaded = await _service.GetSessionAsync(session.Id);
        Assert.Equal(2, loaded!.Iterations.Count);
        var active = Assert.Single(loaded.Iterations, i => i.IsActive);
        Assert.Equal("darker", active.InstructionText);
    }

    [Fact]
    public async Task ThrowingSubscriber_DoesNotBreakTheRun()
    {
        var session = await _service.StartSessionAsync("desc", null, "claude-sonnet-5", []);
        _coordinator.Changed += _ => throw new InvalidOperationException("bad subscriber");

        Assert.True(_coordinator.TryStartCandidates(session.Id, 1));
        _generator.Release();
        await WaitForAsync(() => _coordinator.GetState(session.Id) is { Running: false });

        Assert.Null(_coordinator.GetState(session.Id)!.Error);
        Assert.Single((await _service.GetSessionAsync(session.Id))!.Iterations);
    }

    [Fact]
    public async Task GeneratorFailure_SurfacesErrorState()
    {
        var session = await _service.StartSessionAsync("desc", null, "claude-sonnet-5", []);
        Assert.True(_coordinator.TryStartCandidates(session.Id, 1));

        _generator.Fail(new InvalidOperationException("backend exploded"));
        await WaitForAsync(() => _coordinator.GetState(session.Id) is { Running: false });

        var state = _coordinator.GetState(session.Id)!;
        Assert.False(state.Cancelled);
        Assert.Contains("exploded", state.Error);

        // Terminal state is dismissable, after which a fresh state is possible.
        _coordinator.ClearTerminal(session.Id);
        Assert.Null(_coordinator.GetState(session.Id));
    }

    public void Dispose()
    {
        _provider.Dispose();
        _factory.Dispose();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
