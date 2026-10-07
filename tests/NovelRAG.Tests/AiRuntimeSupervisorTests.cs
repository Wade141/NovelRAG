using NovelRAG.Application;
using Xunit;

namespace NovelRAG.Tests;

public sealed class AiRuntimeSupervisorTests
{
    [Fact]
    public async Task Two_consecutive_http_failures_trigger_recovery_but_success_resets_threshold()
    {
        var probe = new Probe(); var calls = 0;
        var supervisor = new AiRuntimeSupervisor(probe, () => true, () => true, (_, _) => { calls++; return Task.FromResult(true); }); supervisor.Activate();
        await supervisor.CheckOnceAsync(); Assert.Equal(0, calls);
        probe.Health = new(true, false); await supervisor.CheckOnceAsync();
        probe.Health = new(false, false); await supervisor.CheckOnceAsync(); Assert.Equal(0, calls);
        await supervisor.CheckOnceAsync(); Assert.Equal(1, calls);
        await supervisor.SuspendAsync();
    }
    [Fact]
    public async Task Owned_exit_is_immediate_success_does_not_reset_budget_and_manual_cycle_does()
    {
        var probe = new Probe { Health = new(false, true) }; var actions = new List<AiRuntimeFailure>();
        var supervisor = new AiRuntimeSupervisor(probe, () => true, () => true, (f, _) => { actions.Add(f); return Task.FromResult(true); }); supervisor.Activate();
        await supervisor.CheckOnceAsync(); await supervisor.CheckOnceAsync(); await supervisor.CheckOnceAsync();
        Assert.Equal(new[] { true, false }, actions.Select(a => a.CanRecover));
        supervisor.Activate(); await supervisor.CheckOnceAsync(); Assert.True(actions[^1].CanRecover); Assert.Equal(3, actions.Count);
        await supervisor.SuspendAsync();
    }
    [Fact]
    public async Task External_mode_reports_unavailable_once_and_never_requests_recovery()
    {
        var probe = new Probe { Health = new(false, true) }; var actions = new List<AiRuntimeFailure>();
        var supervisor = new AiRuntimeSupervisor(probe, () => true, () => false, (f, _) => { actions.Add(f); return Task.FromResult(false); }); supervisor.Activate();
        await supervisor.CheckOnceAsync(); await supervisor.CheckOnceAsync();
        var action = Assert.Single(actions); Assert.False(action.Managed); Assert.False(action.CanRecover); await supervisor.SuspendAsync();
    }
    [Fact]
    public async Task Parallel_checks_join_boundary_and_suspension_cancels_and_waits_for_recovery()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var supervisor = new AiRuntimeSupervisor(new Probe { Health = new(false, true) }, () => true, () => true, async (_, ct) =>
        {
            calls++; entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { canceled.TrySetResult(); await release.Task; throw; }
            return true;
        }); supervisor.Activate();
        var checks = Enumerable.Range(0, 8).Select(_ => supervisor.CheckOnceAsync()).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); var stopping = supervisor.SuspendAsync();
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.False(stopping.IsCompleted); release.TrySetResult();
        await stopping; await Task.WhenAll(checks); Assert.Equal(1, calls);
        await supervisor.CheckOnceAsync(); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task Maintenance_resume_preserves_spent_budget_and_unready_state_skips_probe()
    {
        var enabled = false; var probe = new Probe { Health = new(false, true) }; var recoveries = 0;
        var supervisor = new AiRuntimeSupervisor(probe, () => enabled, () => true, (f, _) => { if (f.CanRecover) recoveries++; return Task.FromResult(true); }); supervisor.Activate();
        await supervisor.CheckOnceAsync(); Assert.Equal(0, probe.Calls);
        enabled = true; await supervisor.CheckOnceAsync(); await supervisor.SuspendAsync(); supervisor.Activate(resetBudget: false);
        await supervisor.CheckOnceAsync(); Assert.Equal(1, recoveries); await supervisor.SuspendAsync();
    }
    [Fact]
    public async Task Failed_recovery_does_not_loop_and_run_uses_injected_five_second_delay()
    {
        var calls = 0; using var stop = new CancellationTokenSource(); var waits = 0;
        var supervisor = new AiRuntimeSupervisor(new Probe { Health = new(false, true) }, () => true, () => true,
            (_, _) => { calls++; return Task.FromResult(false); }, (duration, ct) =>
            { Assert.Equal(TimeSpan.FromSeconds(5), duration); if (++waits == 4) stop.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        supervisor.Activate(); await supervisor.RunAsync(stop.Token); Assert.Equal(1, calls); await supervisor.SuspendAsync();
    }
    private sealed class Probe : IAiRuntimeHealthProbe
    {
        public AiRuntimeHealth Health = new(false, false); public int Calls;
        public Task<AiRuntimeHealth> CheckAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(Health); }
    }
}
