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
        public bool HoldEngine, ChatFails, DownloadAllowed;
        public IndexProgress Index = new(2, 2, 0);
        public TaskCompletionSource EngineEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task CheckAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
        { DownloadAllowed = allowDownload; return Task.CompletedTask; }
        public async Task PrepareEngineAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
        { EngineEntered.TrySetResult(); if (HoldEngine) await Task.Delay(Timeout.Infinite, ct); }
        public Task PrepareSearchAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct) { needsSearchCheck = false; return Task.CompletedTask; }
        public Task PrepareChatAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
        { if (RestartDuringChat) { needsSearchCheck = true; RuntimeInvalidated?.Invoke(); } return ChatFails ? Task.FromException(new IOException("问答组件不可用")) : Task.CompletedTask; }
        public Task<IndexProgress> IndexAsync(IProgress<IndexProgress> progress, CancellationToken ct) => needsSearchCheck ? Task.FromException<IndexProgress>(new IOException("embedding not checked for this runtime")) : Task.FromResult(Index);
        public Task SaveStateAsync(AiPreparationStatus state, CancellationToken ct) => Task.CompletedTask;
        public Task SetEnabledAsync(bool enabled, CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
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
