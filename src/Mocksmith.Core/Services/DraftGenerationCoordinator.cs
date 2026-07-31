using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mocksmith.Core.Generation;

namespace Mocksmith.Core.Services;

public sealed record GenerationRunState(
    Guid SessionId,
    string Kind,
    bool Running,
    string? Phase,
    string? Error,
    bool Cancelled,
    DateTime StartedAtUtc);

/// <summary>
/// Owns long-running generation work independently of any Blazor circuit: a run started
/// here keeps going when the user navigates away, and a revisiting workspace re-attaches
/// via <see cref="GetState"/> + <see cref="Changed"/>. One run per session at a time;
/// cancellation is an explicit user action, never a navigation side effect. Each run
/// executes inside its own DI scope so scoped services never outlive their circuit.
/// </summary>
public class DraftGenerationCoordinator(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<DraftGenerationCoordinator>? logger = null)
{
    private sealed class Run
    {
        public required GenerationRunState State { get; set; }
        public CancellationTokenSource? Cts { get; set; }
        public object Sync { get; } = new();
    }

    private readonly ConcurrentDictionary<Guid, Run> _runs = new();

    /// <summary>Fired after every state transition with the affected session id (thread-pool thread).</summary>
    public event Action<Guid>? Changed;

    public GenerationRunState? GetState(Guid sessionId)
        => _runs.TryGetValue(sessionId, out var run) ? ReadState(run) : null;

    public bool TryStartCandidates(Guid sessionId, int fanOut)
        => TryStart(sessionId, "candidates",
            (sessions, progress, ct) => sessions.GenerateCandidatesAsync(sessionId, fanOut, progress, ct));

    public bool TryStartRefine(Guid sessionId, string instruction)
        => TryStart(sessionId, "refine",
            (sessions, progress, ct) => sessions.RefineAsync(sessionId, instruction, progress, ct));

    public void Cancel(Guid sessionId)
    {
        if (_runs.TryGetValue(sessionId, out var run))
        {
            lock (run.Sync)
            {
                run.Cts?.Cancel();
            }
        }
    }

    /// <summary>Drops a finished run's state once the UI has shown its error/cancel notice.</summary>
    public void ClearTerminal(Guid sessionId)
    {
        if (_runs.TryGetValue(sessionId, out var run) && !ReadState(run).Running)
        {
            _runs.TryRemove(sessionId, out _);
        }
    }

    private static GenerationRunState ReadState(Run run)
    {
        lock (run.Sync)
        {
            return run.State;
        }
    }

    private bool TryStart(
        Guid sessionId,
        string kind,
        Func<DraftSessionService, IProgress<GenerationProgress>, CancellationToken, Task> work)
    {
        var cts = new CancellationTokenSource();
        var run = new Run
        {
            State = new GenerationRunState(
                sessionId, kind, Running: true, Phase: null, Error: null, Cancelled: false,
                timeProvider.GetUtcNow().UtcDateTime),
            Cts = cts,
        };

        while (true)
        {
            if (_runs.TryAdd(sessionId, run))
            {
                break;
            }

            if (_runs.TryGetValue(sessionId, out var existing))
            {
                if (ReadState(existing).Running)
                {
                    cts.Dispose();
                    return false;
                }

                if (_runs.TryUpdate(sessionId, run, existing))
                {
                    break;
                }
            }
        }

        var ct = cts.Token;
        // Constructed without an ambient SynchronizationContext, so callbacks arrive on the
        // thread pool. Mutations target this run instance directly: a straggler callback from
        // a finished run can only touch its own orphaned state, never a newer run's.
        var progress = new Progress<GenerationProgress>(p =>
            Update(run, sessionId, s => s with
            {
                Phase = p.Phase + (p.OutputTokensSoFar > 0 ? $" (~{p.OutputTokensSoFar} tokens)" : ""),
            }));

        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sessions = scope.ServiceProvider.GetRequiredService<DraftSessionService>();
                await work(sessions, progress, ct);
                Update(run, sessionId, s => s with { Running = false, Phase = null });
            }
            catch (OperationCanceledException)
            {
                Update(run, sessionId, s => s with { Running = false, Cancelled = true, Phase = null });
            }
            catch (Exception ex)
            {
                Update(run, sessionId, s => s with { Running = false, Error = ex.Message, Phase = null });
            }
            finally
            {
                lock (run.Sync)
                {
                    run.Cts = null;
                    cts.Dispose();
                }
            }
        });

        RaiseChanged(sessionId);
        return true;
    }

    private void Update(Run run, Guid sessionId, Func<GenerationRunState, GenerationRunState> mutate)
    {
        lock (run.Sync)
        {
            run.State = mutate(run.State);
        }

        RaiseChanged(sessionId);
    }

    /// <summary>
    /// Invokes subscribers one by one, isolating failures: Changed fires from thread-pool
    /// callbacks, where an unhandled subscriber exception would crash the process and take
    /// coordinator-owned background work with it.
    /// </summary>
    private void RaiseChanged(Guid sessionId)
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<Guid>>())
        {
            try
            {
                handler(sessionId);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Generation state subscriber threw for session {SessionId}.", sessionId);
            }
        }
    }
}
