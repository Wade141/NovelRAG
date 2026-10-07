using NovelRAG.Application;
using NovelRAG.Domain;
using Xunit;

namespace NovelRAG.Tests;

public sealed class AiPreparationTests
{
    [Fact]
    public async Task Repeated_clicks_join_one_task_and_pause_can_resume()
    {
        var backend = new Backend { HoldEngine = true };
        var service = new AiPreparationService(backend);
        var first = service.StartAsync();
        await backend.EngineEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Same(first, service.StartAsync());
        await service.PauseAsync();
        Assert.Equal(AiStage.Paused, service.Status.Stage);
        backend.HoldEngine = false;
        await service.StartAsync();
        Assert.Equal(AiStage.Ready, service.Status.Stage);
        Assert.True(service.Status.SearchReady && service.Status.ChatReady);
    }

    [Fact]
    public async Task Chat_failure_preserves_search_and_indexes_notes()
    {
        var backend = new Backend { ChatFails = true };
        var service = new AiPreparationService(backend);
        await service.StartAsync();
        Assert.Equal(AiStage.Partial, service.Status.Stage);
        Assert.True(service.Status.SearchReady);
        Assert.False(service.Status.ChatReady);
        Assert.Equal(2, service.Status.Indexed);
        service.UpdateIndex(new(3, 3, 0));
        Assert.Contains("问答暂不可用", service.Status.Message); Assert.Equal(AiStage.Partial, service.Status.Stage);
    }

    [Fact]
    public async Task Failed_or_unprocessed_notes_do_not_report_ready()
    {
        var service = new AiPreparationService(new Backend { Index = new(2, 5, 1) });
        await service.StartAsync();
        Assert.Equal(AiStage.Partial, service.Status.Stage);
        Assert.Equal(4, service.Status.Pending);
        Assert.Equal(1, service.Status.Failed);
    }

    [Fact]
    public async Task Maintenance_blocks_new_preparation_and_shutdown_waits_for_cancel()
    {
        var backend = new Backend { HoldEngine = true };
        var service = new AiPreparationService(backend);
        _ = service.StartAsync();
        await backend.EngineEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await service.SuspendAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync());
        service.ReleaseMaintenance();
        backend.HoldEngine = false;
        await service.StartAsync();
        Assert.Equal(AiStage.Ready, service.Status.Stage);
    }

    [Fact]
    public async Task Restart_does_not_trust_persisted_capabilities_and_offline_start_forbids_downloads()
    {
        var backend = new Backend();
        var service = new AiPreparationService(backend);
        service.Restore(new(AiStage.Ready, "旧状态", SearchReady: true, ChatReady: true));
        Assert.False(service.Status.SearchReady || service.Status.ChatReady);
        await service.StartAsync(allowDownload: false);
        Assert.False(backend.DownloadAllowed);
        Assert.Equal(AiStage.Ready, service.Status.Stage);
    }

    private sealed class Backend : IAiPreparationBackend
    {
        public event Action? RuntimeInvalidated;
        public bool RestartDuringChat;
        private bool needsSearchCheck;
        public bool HoldEngine, ChatFails, DownloadAllowed, Enabled;
        public int EngineCalls, StopCalls;
        public IndexProgress Index = new(2, 2, 0);
        public TaskCompletionSource EngineEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task CheckAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
        { DownloadAllowed = allowDownload; return Task.CompletedTask; }
        public async Task PrepareEngineAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
        { EngineCalls++; EngineEntered.TrySetResult(); if (HoldEngine) await Task.Delay(Timeout.Infinite, ct); }
        public Task PrepareSearchAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct) { needsSearchCheck = false; return Task.CompletedTask; }
        public Task PrepareChatAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
        { if (RestartDuringChat) { needsSearchCheck = true; RuntimeInvalidated?.Invoke(); } return ChatFails ? Task.FromException(new IOException("问答组件不可用")) : Task.CompletedTask; }
        public Task<IndexProgress> IndexAsync(IProgress<IndexProgress> progress, CancellationToken ct) => needsSearchCheck ? Task.FromException<IndexProgress>(new IOException("embedding not checked for this runtime")) : Task.FromResult(Index);
        public Task SaveStateAsync(AiPreparationStatus state, CancellationToken ct) => Task.CompletedTask;
        public Task SetEnabledAsync(bool enabled, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Enabled = enabled; return Task.CompletedTask; }
        public Task StopAsync() { StopCalls++; RuntimeInvalidated?.Invoke(); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Successful_compatible_restart_revalidates_search_before_indexing()
    {
        var service = new AiPreparationService(new Backend { RestartDuringChat = true });
        await service.StartAsync();
        Assert.Equal(AiStage.Ready, service.Status.Stage);
        Assert.True(service.Status.SearchReady && service.Status.ChatReady);
    }
    [Theory]
    [InlineData("disable")]
    [InlineData("shutdown")]
    [InlineData("restore")]
    [InlineData("directory")]
    public async Task Lifecycle_boundary_cancels_inflight_recovery_before_stopping_or_maintenance(string action)
    {
        var backend = new Backend(); var preparation = new AiPreparationService(backend); await preparation.StartAsync(false);
        backend.HoldEngine = true; backend.EngineEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var supervisor = new AiRuntimeSupervisor(new ExitedProbe(), () => true, () => true, async (failure, ct) =>
        {
            preparation.RuntimeUnavailable(failure); await preparation.StartAsync(false, ct); ct.ThrowIfCancellationRequested(); return preparation.Status.SearchReady;
        }); supervisor.Activate(); var recovery = supervisor.CheckOnceAsync();
        await backend.EngineEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(preparation.Status.SearchReady || preparation.Status.ChatReady); Assert.False(backend.DownloadAllowed);
        await supervisor.SuspendAsync();
        if (action == "disable") await preparation.DisableAsync();
        else if (action == "restore") await preparation.SuspendAsync();
        else await preparation.ShutdownAsync();
        await recovery; await supervisor.CheckOnceAsync(); Assert.Equal(2, backend.EngineCalls);
        Assert.False(preparation.IsRunning); Assert.False(preparation.Status.SearchReady || preparation.Status.ChatReady);
        if (action == "disable") Assert.False(backend.Enabled);
        if (action != "disable") await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.StartAsync(false));
        if (action != "restore") Assert.Equal(1, backend.StopCalls);
    }
    private sealed class ExitedProbe : IAiRuntimeHealthProbe
    {
        public Task<AiRuntimeHealth> CheckAsync(CancellationToken ct) => Task.FromResult(new AiRuntimeHealth(false, true));
    }
    [Fact]
    public async Task Runtime_failure_revokes_old_capabilities_and_recheck_can_restore_only_search()
    {
        var backend = new Backend(); var service = new AiPreparationService(backend); await service.StartAsync(false);
        service.RuntimeUnavailable(new(true, true));
        Assert.Equal(AiStage.Recovering, service.Status.Stage); Assert.False(service.Status.SearchReady || service.Status.ChatReady);
        backend.ChatFails = true; await service.StartAsync(false);
        Assert.True(service.Status.SearchReady); Assert.False(service.Status.ChatReady); Assert.Equal(AiStage.Partial, service.Status.Stage);
        service.RuntimeUnavailable(new(true, false)); Assert.Contains("检查与修复", service.Status.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Runtime_restart_invalidates_old_search_until_revalidated(bool cancel)
    {
        var backend = new RestartBackend(cancel);
        var service = new AiPreparationService(backend);
        _ = service.StartAsync(); await backend.Restarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(service.Status.SearchReady);
        if (cancel) await service.PauseAsync(); else { backend.Fail.TrySetResult(); await service.StartAsync(); }
        Assert.False(service.Status.SearchReady || service.Status.ChatReady);
    }
    private sealed class RestartBackend(bool cancel) : IAiPreparationBackend
    {
        private bool unavailable;
        public event Action? RuntimeInvalidated;
        public TaskCompletionSource Restarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Fail = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task CheckAsync(bool download, IProgress<AiTransferProgress> p, CancellationToken ct) => Task.CompletedTask;
        public Task PrepareEngineAsync(bool download, IProgress<AiTransferProgress> p, CancellationToken ct) => Task.CompletedTask;
        public Task PrepareSearchAsync(bool download, IProgress<AiTransferProgress> p, CancellationToken ct) => unavailable ? Task.FromException(new IOException("runtime absent")) : Task.CompletedTask;
        public async Task PrepareChatAsync(bool download, IProgress<AiTransferProgress> p, CancellationToken ct)
        {
            unavailable = true; RuntimeInvalidated?.Invoke(); Restarted.TrySetResult();
            if (cancel) await Task.Delay(Timeout.Infinite, ct); else { await Fail.Task; throw new IOException("runtime restart failed"); }
        }
        public Task<IndexProgress> IndexAsync(IProgress<IndexProgress> p, CancellationToken ct) => Task.FromException<IndexProgress>(new IOException("no runtime"));
        public Task SaveStateAsync(AiPreparationStatus s, CancellationToken ct) => Task.CompletedTask;
        public Task SetEnabledAsync(bool enabled, CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }
}
