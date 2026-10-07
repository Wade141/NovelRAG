using NovelRAG.Domain;

namespace NovelRAG.Application;

public enum AiStage { Idle, Checking, Engine, Search, Chat, Indexing, Ready, Partial, Paused, Failed, Disabled, Recovering }
public sealed record AiTransferProgress(string Message, long Completed = 0, long Total = 0, double BytesPerSecond = 0);
public sealed record AiPreparationStatus(AiStage Stage, string Message, bool SearchReady = false, bool ChatReady = false,
    int Indexed = 0, int Pending = 0, int Failed = 0, AiTransferProgress? Transfer = null, string? Detail = null);
public interface IAiPreparationBackend
{
    event Action? RuntimeInvalidated { add { } remove { } }
    Task CheckAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct);
    Task PrepareEngineAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct);
    Task PrepareSearchAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct);
    Task PrepareChatAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct);
    Task<IndexProgress> IndexAsync(IProgress<IndexProgress> progress, CancellationToken ct);
    Task SaveStateAsync(AiPreparationStatus state, CancellationToken ct);
    Task SetEnabledAsync(bool enabled, CancellationToken ct);
    Task StopAsync();
}
public sealed class AiPreparationService
{
    private readonly IAiPreparationBackend backend;
    public AiPreparationService(IAiPreparationBackend backend)
    {
        this.backend = backend;
        backend.RuntimeInvalidated += () => Set(Status with { SearchReady = false, ChatReady = false });
    }
    private readonly object gate = new();
    private AiPreparationStatus status = new(AiStage.Idle, "尚未准备 AI");
    private CancellationTokenSource? cancellation;
    private Task? active;
    private bool suspended;
    public event Action? Changed;
    public AiPreparationStatus Status { get { lock (gate) return status; } }
    public bool IsRunning { get { lock (gate) return active is { IsCompleted: false }; } }
    public Task StartAsync(bool allowDownload = true, CancellationToken ct = default)
    {
        lock (gate)
        {
            if (suspended) return Task.FromException(new InvalidOperationException("正在维护资料库或退出，请稍后再准备 AI。"));
            if (active is { IsCompleted: false }) return active;
            cancellation?.Dispose(); cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct); var token = cancellation.Token;
            active = Task.Run(() => RunAsync(allowDownload, token)); return active;
        }
    }
    public async Task PauseAsync()
    {
        Task? task;
        lock (gate) { cancellation?.Cancel(); task = active; }
        if (task is not null) await task.ConfigureAwait(false);
    }
    public async Task SuspendAsync()
    {
        lock (gate) suspended = true;
        await PauseAsync().ConfigureAwait(false);
    }
    public void ReleaseMaintenance() { lock (gate) suspended = false; }
    public async Task ShutdownAsync() { await SuspendAsync().ConfigureAwait(false); await backend.StopAsync().ConfigureAwait(false); }
    public async Task DisableAsync()
    {
        await SuspendAsync().ConfigureAwait(false);
        try
        {
            await backend.SetEnabledAsync(false, CancellationToken.None).ConfigureAwait(false);
            await backend.StopAsync().ConfigureAwait(false);
            Set(new(AiStage.Disabled, "AI 已关闭；记录与关键词搜索正常可用"));
            await backend.SaveStateAsync(Status, CancellationToken.None).ConfigureAwait(false);
        }
        finally { ReleaseMaintenance(); }
    }
    public void Restore(AiPreparationStatus? state)
    {
        if (state is not null) Set(new(state.Stage == AiStage.Disabled ? AiStage.Disabled : AiStage.Paused,
            state.Stage == AiStage.Disabled ? "AI 已关闭" : "可继续准备；已下载的组件会重新检查并复用"));
    }
    public void RuntimeUnavailable(AiRuntimeFailure failure)
    {
        Set(Status with
        {
            Stage = failure.CanRecover ? AiStage.Recovering : AiStage.Failed,
            SearchReady = false,
            ChatReady = false,
            Transfer = null,
            Detail = null,
            Message = failure.CanRecover ? "本地 AI 已中断，正在尝试自动恢复一次；记录与关键词搜索可用"
                : failure.Managed ? "本地 AI 再次中断或恢复失败；请点击“检查与修复”，记录与关键词搜索可用"
                : "已有本机 AI 服务不可用；请先恢复该服务，再点击“检查与修复”。记录与关键词搜索可用"
        });
    }
    public void UpdateIndex(IndexProgress progress)
    {
        lock (gate)
        {
            if (active is { IsCompleted: false } || !status.SearchReady) return;
            var pending = Math.Max(0, progress.Total - progress.Completed + progress.Failed);
            status = status with
            {
                Indexed = progress.Completed - progress.Failed,
                Pending = pending,
                Failed = progress.Failed,
                Stage = status.ChatReady && pending == 0 ? AiStage.Ready : AiStage.Partial,
                Message = pending == 0 ? status.ChatReady ? "灵感整理完成，智能搜索与问答可用" : "灵感整理完成，智能搜索可用；问答暂不可用，可继续准备"
                    : $"仍有 {pending} 条灵感等待整理，可继续准备或修复"
            };
        }
        Changed?.Invoke();
    }
    private void Set(AiPreparationStatus value) { lock (gate) status = value; Changed?.Invoke(); }
    private async Task StageAsync(AiStage stage, string message, CancellationToken ct)
    {
        Set(Status with { Stage = stage, Message = message, Transfer = null });
        await backend.SaveStateAsync(Status, ct).ConfigureAwait(false);
    }
    private async Task RunAsync(bool allowDownload, CancellationToken ct)
    {
        var transfer = new InlineProgress<AiTransferProgress>(p => Set(Status with { Transfer = p }));
        Set(new(AiStage.Checking, "正在检查电脑与可复用组件…"));
        try
        {
            await backend.SetEnabledAsync(true, ct).ConfigureAwait(false);
            await backend.CheckAsync(allowDownload, transfer, ct).ConfigureAwait(false);
            await StageAsync(AiStage.Engine, "正在准备本地 AI 引擎…", ct).ConfigureAwait(false);
            await backend.PrepareEngineAsync(allowDownload, transfer, ct).ConfigureAwait(false);
            await StageAsync(AiStage.Search, "正在准备智能搜索组件…", ct).ConfigureAwait(false);
            await backend.PrepareSearchAsync(allowDownload, transfer, ct).ConfigureAwait(false);
            Set(Status with { SearchReady = true });
            await StageAsync(AiStage.Chat, "智能搜索已可用，正在准备问答组件…", ct).ConfigureAwait(false);
            string? chatError = null;
            try { await backend.PrepareChatAsync(allowDownload, transfer, ct).ConfigureAwait(false); Set(Status with { ChatReady = true }); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { chatError = ex.Message; }
            ct.ThrowIfCancellationRequested();
            if (!Status.SearchReady)
            {
                await backend.PrepareSearchAsync(false, transfer, ct).ConfigureAwait(false);
                Set(Status with { SearchReady = true });
            }
            await StageAsync(AiStage.Indexing, "正在整理已有灵感…", ct).ConfigureAwait(false);
            var result = await backend.IndexAsync(new InlineProgress<IndexProgress>(p => Set(Status with
            {
                Indexed = p.Completed - p.Failed,
                Pending = Math.Max(0, p.Total - p.Completed + p.Failed),
                Failed = p.Failed,
                Message = $"正在整理灵感 {p.Completed} / {p.Total} · 失败 {p.Failed}"
            })), ct).ConfigureAwait(false);
            var pending = Math.Max(0, result.Total - result.Completed + result.Failed);
            Set(Status with
            {
                Stage = chatError is null && pending == 0 ? AiStage.Ready : AiStage.Partial,
                Message = chatError is not null ? "智能搜索可用，问答暂不可用；点击继续准备可重试" : pending > 0 ? $"AI 可用，仍有 {pending} 条灵感等待整理" : "AI 已准备好，可以开始提问了",
                Indexed = result.Completed - result.Failed,
                Pending = pending,
                Failed = result.Failed,
                Transfer = null,
                Detail = chatError
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { Set(Status with { Stage = AiStage.Paused, Message = "已暂停，已完成部分会保留", Transfer = null }); }
        catch (Exception ex)
        { Set(Status with { Stage = Status.SearchReady ? AiStage.Partial : AiStage.Failed, Message = "准备未完成；记录与关键词搜索仍可用。可点击继续准备。", Transfer = null, Detail = ex.Message }); }
        try { await backend.SaveStateAsync(Status, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { Set(Status with { Stage = AiStage.Failed, Message = "无法保存准备进度，请检查资料目录权限后重试", Detail = ex.Message }); }
        Changed?.Invoke();
    }
}

// Unlike Progress<T>, reports are ordered and cannot arrive after a later stage.
public sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
