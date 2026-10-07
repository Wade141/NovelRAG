using System.Diagnostics;

namespace NovelRAG.Application;

public sealed record AiRuntimeHealth(bool Healthy, bool OwnedProcessExited);
public interface IAiRuntimeHealthProbe { Task<AiRuntimeHealth> CheckAsync(CancellationToken ct); }
public sealed record AiRuntimeFailure(bool Managed, bool CanRecover);

public sealed class AiRuntimeSupervisor(IAiRuntimeHealthProbe probe, Func<bool> canCheck, Func<bool> managed,
    Func<AiRuntimeFailure, CancellationToken, Task<bool>> handleFailure, Func<TimeSpan, CancellationToken, Task>? delay = null, IAppLog? log = null)
{
    private readonly SemaphoreSlim checkGate = new(1, 1);
    private readonly object stateGate = new();
    private CancellationTokenSource? cycle;
    private bool enabled, spent;
    private int failures, suspensions, running;
    public void Activate(bool resetBudget = true)
    {
        lock (stateGate)
        {
            if (suspensions != 0) throw new InvalidOperationException("正在暂停 AI 健康检查。");
            if (enabled) { if (!resetBudget) return; throw new InvalidOperationException("请先暂停健康检查，再开始新的恢复周期。"); }
            cycle?.Dispose(); cycle = new(); enabled = true; failures = 0;
            if (resetBudget) spent = false;
        }
    }
    public async Task SuspendAsync()
    {
        CancellationTokenSource? current;
        lock (stateGate) { suspensions++; enabled = false; current = cycle; }
        try
        {
            if (current is not null) await current.CancelAsync().ConfigureAwait(false);
            await checkGate.WaitAsync().ConfigureAwait(false); checkGate.Release();
        }
        finally { lock (stateGate) suspensions--; }
    }
    public async Task CheckOnceAsync(CancellationToken ct = default)
    {
        await checkGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CancellationToken token;
            lock (stateGate) { if (!enabled || cycle is null) return; token = cycle.Token; }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!canCheck()) { failures = 0; return; }
                AiRuntimeHealth health;
                try { health = await probe.CheckAsync(linked.Token).ConfigureAwait(false); }
                catch (Exception ex) when (!linked.IsCancellationRequested) { log?.Write("ai.health_probe_failed", ex); health = new(false, false); }
                linked.Token.ThrowIfCancellationRequested();
                if (health.Healthy) { failures = 0; return; }
                if (!health.OwnedProcessExited && ++failures < 2) return;
                failures = 0;
                var owned = managed(); var recover = owned && !spent;
                if (recover) spent = true;
                log?.Write("ai.runtime_unavailable", data: new { Managed = owned, AutomaticRecovery = recover });
                var watch = Stopwatch.StartNew();
                if (recover) log?.Write("ai.runtime.recovery_started");
                bool restored;
                try
                {
                    restored = await handleFailure(new(owned, recover), linked.Token).ConfigureAwait(false);
                    linked.Token.ThrowIfCancellationRequested();
                    if (recover) log?.Write(restored ? "ai.runtime.recovery_succeeded" : "ai.runtime.recovery_failed", data: new { ElapsedMs = watch.ElapsedMilliseconds, Canceled = false });
                }
                catch (Exception ex)
                {
                    if (recover) log?.Write("ai.runtime.recovery_failed", ex, new { ElapsedMs = watch.ElapsedMilliseconds, Canceled = linked.IsCancellationRequested });
                    throw;
                }
                if (!recover || !restored) { lock (stateGate) enabled = false; }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
            catch (Exception ex) { lock (stateGate) enabled = false; log?.Write("ai.recovery_failed", ex); }
        }
        finally { checkGate.Release(); }
    }
    public async Task RunAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("健康监测已在运行。");
        try
        {
            while (true)
            {
                await (delay ?? Task.Delay)(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                await CheckOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { await SuspendAsync().ConfigureAwait(false); Interlocked.Exchange(ref running, 0); }
    }
}
