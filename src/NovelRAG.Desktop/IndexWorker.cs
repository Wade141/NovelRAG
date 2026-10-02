namespace NovelRAG.Desktop;
// All automatic indexing is owned here, so restore can cancel and await it.
public sealed class IndexWorker(AppServices services) : IAsyncDisposable
{
    private CancellationTokenSource? stop;
    private Task? task;
    private readonly SemaphoreSlim wake = new(0, 1);
    public string Status { get; private set; } = "尚未启用语义索引";
    public void Request() { if (wake.CurrentCount == 0) try { wake.Release(); } catch (SemaphoreFullException) { } }
    public void Start()
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
    public async Task StopAsync()
    {
        if (stop is null) return; await stop.CancelAsync();
        if (task is not null) try { await task; } catch (OperationCanceledException) { }
        stop.Dispose(); stop = null; task = null;
    }
    public async ValueTask DisposeAsync() { await StopAsync(); wake.Dispose(); }
}
