using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NovelRAG.Application;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;

namespace NovelRAG.AI.Ollama;

public sealed class AiPreparationBackend : IAiPreparationBackend, IDisposable
{
    private readonly string dataDirectory;
    private readonly Func<AppSettings> settings;
    private readonly Func<Func<AppSettings, AppSettings>, Task> update;
    private readonly Func<IProgress<IndexProgress>, CancellationToken, Task<IndexProgress>> index;
    private readonly IAppLog log;
    private readonly HttpClient network = new(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 8 }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient local = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly ManagedOllamaRuntime runtime = new();
    private readonly ComponentCatalog catalog = ComponentCatalog.Load();
    private readonly AiComponentInstaller installer;
    private readonly AiStateStore state;
    private readonly OllamaProvider probe;
    private bool cpuOnly;
    private string? executable;
    public event Action? RuntimeInvalidated;
    public string Root => settings().AiDirectory ?? Path.Combine(dataDirectory, "ai");
    public AiPreparationBackend(string dataDirectory, Func<AppSettings> settings, Func<Func<AppSettings, AppSettings>, Task> update,
        Func<IProgress<IndexProgress>, CancellationToken, Task<IndexProgress>> index, IAppLog log, Func<string, long>? availableSpace = null)
    {
        this.dataDirectory = dataDirectory; this.settings = settings; this.update = update; this.index = index; this.log = log;
        installer = new(catalog, new(network), availableSpace); state = new(Path.Combine(dataDirectory, "ai-preparation.json"));
        probe = new(local, () => { var s = settings(); return new(true, s.Endpoint, s.EmbeddingModel, s.ChatModel); });
        network.DefaultRequestHeaders.UserAgent.ParseAdd("NovelRAG/1.1");
    }
    public Task<AiPreparationStatus?> LoadStateAsync() => state.LoadAsync();
    public AiPreflight Inspect()
    {
        var s = settings();
        if (!s.ManagedAi && (s.AiSetupChosen || s.AiEnabled)) return new(0, 0, 0, 0, Root, true, "使用已有的本机服务；只检查连接和功能，不下载组件或管理其进程。");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19045) || RuntimeInformation.OSArchitecture != Architecture.X64)
            return new(0, 0, 0, 0, Root, false, "一键准备需要 Windows 10 22H2 或更新版本的 x64 电脑。记录与关键词搜索仍可用。", Supported: false);
        return installer.Inspect(Root);
    }
    public Task CheckAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var check = Inspect(); if (!check.Supported) throw new InvalidOperationException(check.Message);
        // Missing chat components must never prevent restoring an installed search engine.
        // Each installation stage checks its own additional writes immediately before use.
        progress.Report(new(check.Message)); return Task.CompletedTask;
    }
    public async Task PrepareEngineAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
    {
        if (!settings().ManagedAi) return;
        RuntimeInvalidated?.Invoke(); await runtime.StopAsync(); cpuOnly = false;
        executable = await installer.PrepareRuntimeAsync(Root, allowDownload, progress, ct);
        var endpoint = await runtime.StartAsync(executable, Root, catalog.Runtime.Version, cpuOnly, ct);
        await SaveConnectionAsync(endpoint);
    }
    private Task SaveConnectionAsync(string endpoint) => update(s => s with
    {
        Endpoint = endpoint,
        EmbeddingModel = catalog.Models[0].Name,
        ChatModel = catalog.Models[1].Name,
        ModelsPath = Path.Combine(Root, "models"),
        AiSetupChosen = true
    });
    public async Task PrepareSearchAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
    {
        if (settings().ManagedAi) await EnsureModelAsync(catalog.Models[0], allowDownload, progress, ct);
        progress.Report(new("正在检查智能搜索是否可用…"));
        await SelfTestAsync(async () => { await probe.EmbedAsync("虚构测试：小鹿在渡口找到铜铃。", ct); }, progress, ct);
    }
    public async Task PrepareChatAsync(bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
    {
        if (settings().ManagedAi)
        {
            if (TotalMemory() < 6UL * 1024 * 1024 * 1024) throw new InvalidOperationException("这台电脑的内存不足以准备当前问答组件。智能搜索仍可用；可在内存更充足的电脑上继续。");
            await EnsureModelAsync(catalog.Models[1], allowDownload, progress, ct);
        }
        progress.Report(new("正在用虚构素材检查本地问答…"));
        await SelfTestAsync(async () =>
        {
            const string text = "小鹿在渡口找到了一枚铜铃。";
            var idea = new Idea(1, null, text, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, 1, []);
            var answer = await probe.AnswerAsync("小鹿在哪里找到什么？", [new(idea, 1, text)], ct);
            if (answer.SourceIds.Count == 0 || answer.SourceIds.Any(id => id != 1) || !answer.Answer.Contains("[#1]", StringComparison.Ordinal) || !answer.Answer.Contains("铜铃", StringComparison.Ordinal))
                throw new InvalidDataException("问答自检未能生成可靠的来源引用，请点击继续准备重试。");
        }, progress, ct);
    }
    private async Task SelfTestAsync(Func<Task> test, IProgress<AiTransferProgress> progress, CancellationToken ct)
    {
        try { await test(); }
        catch (Exception ex) when (!ct.IsCancellationRequested && settings().ManagedAi && !cpuOnly)
        {
            log.Write("ai.selftest_retry_cpu", ex); progress.Report(new("首次加载未成功，正在用兼容模式重试…"));
            cpuOnly = true;
            RuntimeInvalidated?.Invoke();
            await SaveConnectionAsync(await runtime.StartAsync(executable!, Root, catalog.Runtime.Version, true, ct));
            await test();
        }
    }
    private async Task EnsureModelAsync(ModelAsset model, bool allowDownload, IProgress<AiTransferProgress> progress, CancellationToken ct)
    {
        var models = Path.Combine(Root, "models"); progress.Report(new("正在校验已有模型文件…"));
        if (await AiComponentInstaller.ModelValidAsync(model, models, ct)) return;
        if (!allowDownload) throw new InvalidOperationException("部分 AI 组件尚未准备或需要修复，请点击继续准备。");
        foreach (var blob in model.Blobs)
        {
            var path = ComponentFiles.SafeChild(models, blob.Path);
            if (File.Exists(path) && !await ComponentFiles.ValidAsync(path, blob.Size, blob.Sha256, ct)) File.Delete(path);
        }
        installer.EnsureModelSpace(model, models);
        foreach (var source in AiComponentInstaller.KnownSources())
            if (await AiComponentInstaller.ImportModelAsync(model, Path.Combine(source, "models"), models, progress, ct)) return;
        installer.EnsureModelSpace(model, models, allowImports: false);
        // Mutable upstream tags must match the pinned manifest BEFORE downloading.
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var name = model.Name.Split(':');
            using var response = await network.GetAsync($"https://registry.ollama.ai/v2/library/{name[0]}/manifests/{name[1]}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token); using var memory = new MemoryStream();
            var buffer = new byte[8192]; int read;
            while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
            { if (memory.Length + read > 1_000_000) throw new InvalidDataException("模型清单过大。"); memory.Write(buffer, 0, read); }
            if (!Convert.ToHexString(SHA256.HashData(memory.ToArray())).Equals(model.Digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("官方模型版本已变化。为避免未经验证的替换，请更新 NovelRAG 后重试；已有功能仍可用。");
        }
        for (var attempt = 0; ; attempt++)
        {
            try { await new OllamaModelDownloader(local).PullAsync(settings().Endpoint, model.Name, progress, ct); break; }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < 2 && ex is HttpRequestException or IOException or OperationCanceledException)
            { log.Write("ai.model_pull_retry", ex); progress.Report(new("下载中断，正在复用已完成部分重试…")); await Task.Delay(1000 * (attempt + 1), ct); }
        }
        if (!await AiComponentInstaller.ModelValidAsync(model, models, ct)) throw new InvalidDataException("模型与固定清单不一致，请继续准备以重新校验并修复。");
    }
    public Task<IndexProgress> IndexAsync(IProgress<IndexProgress> progress, CancellationToken ct) => index(progress, ct);
    public Task SaveStateAsync(AiPreparationStatus value, CancellationToken ct) => state.SaveAsync(value, ct);
    public Task SetEnabledAsync(bool enabled, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return update(s => s with { AiEnabled = enabled, AiSetupChosen = true }); }
    public Task StopAsync() { RuntimeInvalidated?.Invoke(); return runtime.StopAsync(); }
    public void Dispose() { runtime.Dispose(); local.Dispose(); network.Dispose(); }
    private static ulong TotalMemory()
    {
        if (!OperatingSystem.IsWindows()) return ulong.MaxValue;
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new InvalidOperationException("无法读取内存信息，请稍后重试。");
        return memory.TotalPhysical;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
