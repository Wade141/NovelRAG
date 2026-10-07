namespace NovelRAG.Desktop;
// All automatic indexing is owned here, so restore can cancel and await it.
public sealed class IndexWorker(AppServices services) : IAsyncDisposable
{
    private CancellationTokenSource? stop;
    private Task? task;
    private Task? stopping;
    private readonly object lifecycle = new();
    private bool disposed;
    private readonly SemaphoreSlim wake = new(0, 1);
    public string Status { get; private set; } = "尚未启用语义索引";
    public void Request() { lock (lifecycle) { if (!disposed && wake.CurrentCount == 0) try { wake.Release(); } catch (SemaphoreFullException) { } } }
    public void Start()
    {
        lock (lifecycle) { if (disposed || stopping is { IsCompleted: false }) return; StartCore(); }
    }
    private void StartCore()
    {
        if (task is { IsCompleted: false }) return;
        stop = new(); var token = stop.Token;
        task = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await wake.WaitAsync(TimeSpan.FromSeconds(60), token);
                    if (!services.Settings.AiEnabled || !services.Preparation.Status.SearchReady || services.Preparation.IsRunning) { Status = "AI 尚未就绪；记录与关键词搜索正常可用"; continue; }
                    await Task.Delay(400, token);
                    var result = await services.Indexer.RebuildAsync(ct: token, missingOnly: true);
                    Status = $"最近索引：{result.Completed}/{result.Total}，失败 {result.Failed}";
                    services.Preparation.UpdateIndex(result);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { Status = "语义索引暂不可用，请检查本机模型"; services.Log.Write("index.background_failed", ex); }
            }
        }, token);
        Request();
    }
    public Task StopAsync()
    {
        lock (lifecycle)
        {
            if (stopping is { IsCompleted: false }) return stopping;
            var source = stop; var worker = task;
            if (source is null) return Task.CompletedTask;
            stopping = Task.Run(async () =>
            {
                try { await source.CancelAsync(); if (worker is not null) try { await worker; } catch (OperationCanceledException) { } }
                finally { lock (lifecycle) { source.Dispose(); stop = null; task = null; } }
            });
            return stopping;
        }
    }
    public async ValueTask DisposeAsync() { await StopAsync(); lock (lifecycle) { disposed = true; wake.Dispose(); } }
}
