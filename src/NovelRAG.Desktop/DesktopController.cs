using Forms = System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;
using NovelRAG.Infrastructure;

namespace NovelRAG.Desktop;

public sealed class DesktopController(AppServices services)
{
    private Forms.NotifyIcon? tray;
    private CaptureWindow? capture;
    private LibraryWindow? library;
    private ChatWindow? chat;
    private SettingsWindow? settings;
    private volatile bool exiting, maintenance, aiChanging, recovering;
    private readonly CancellationTokenSource supervisionStop = new();
    private Task supervisionTask = Task.CompletedTask;
    private AiRuntimeSupervisor? supervisor;
    public bool AiTransitioning => exiting || maintenance || aiChanging || recovering;
    private bool CanMonitor => !maintenance && !exiting && !aiChanging && services.Settings.AiEnabled && !services.Preparation.IsRunning && services.Preparation.Status.SearchReady;
    public AiRuntimeSupervisor Supervisor => supervisor ??= new(services.AiBackend, () => CanMonitor, () => services.Settings.ManagedAi, DispatchRuntimeFailureAsync, log: services.Log);
    public PetWindow Pet { get; private set; } = null!;
    public IndexWorker Worker { get; } = new(services);
    public void Start()
    {
        Pet = new(services, this, () => new OwlPetRenderer());
        if (services.Settings.PetVisible) Pet.Show();
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "NovelRAG · 灵感随手记", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        void Add(string name, Action action) => menu.Items.Add(name, null, (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(action));
        Add("记录灵感", OpenCapture); Add("搜索 / 提问", OpenChat); Add("灵感库", () => OpenLibrary());
        Add("显示 / 隐藏桌宠", () => _ = TogglePetAsync()); Add("设置", OpenSettings); menu.Items.Add(new Forms.ToolStripSeparator()); Add("退出", () => _ = ExitAsync());
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => OpenCapture();
        services.Ideas.Changed += Worker.Request; Worker.Start();
        supervisionTask = Supervisor.RunAsync(supervisionStop.Token);
        if (services.Settings.AiEnabled) _ = StartAiPreparationAsync(false);
    }
    public ContextMenu CreatePetMenu()
    {
        var menu = new ContextMenu();
        void Add(string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("记录灵感", OpenCapture); Add("搜索 / 提问", OpenChat); Add("灵感库", () => OpenLibrary()); Add("设置", OpenSettings);
        Add("隐藏桌宠", () => _ = TogglePetAsync()); menu.Items.Add(new Separator()); Add("退出", () => _ = ExitAsync()); return menu;
    }
    private static void Activate(Window w) { w.Show(); if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal; w.Activate(); }
    public void OpenCapture()
    {
        if (maintenance || exiting) return;
        if (capture is null) { capture = new(services); capture.Closed += (_, _) => capture = null; }
        Activate(capture);
    }
    public void OpenLibrary(long? id = null)
    {
        if (maintenance || exiting) return;
        if (library is null) { library = new(services, this); library.Closed += (_, _) => library = null; }
        Activate(library); if (id is not null) _ = library.OpenIdeaAsync(id.Value);
    }
    public void OpenChat()
    {
        if (maintenance || exiting) return;
        if (chat is null) { chat = new(services, this); chat.Closed += (_, _) => chat = null; }
        Activate(chat);
    }
    public void OpenSettings()
    {
        if (maintenance || exiting) return;
        if (settings is null) { settings = new(services, this); settings.Closed += (_, _) => settings = null; }
        Activate(settings);
    }
    public async Task TogglePetAsync()
    {
        try { await services.UpdateSettingsAsync(s => s with { PetVisible = !s.PetVisible }); Pet.ApplySettings(); await Pet.RendererTask; }
        catch (Exception ex) { Ui.Error(ex); }
    }
    public void ApplySettings() { Pet.ApplySettings(); Worker.Request(); }
    public async Task StartAiPreparationAsync(bool allowDownload = true)
    {
        if (maintenance || exiting || aiChanging || services.Preparation.IsRunning) return;
        aiChanging = true;
        Task? preparationTask = null;
        try
        {
            await Supervisor.SuspendAsync(); await CancelChatAsync();
            await Worker.StopAsync();
            if (maintenance || exiting) return;
            if (!services.Settings.AiSetupChosen && !services.Settings.AiEnabled)
                await services.UpdateSettingsAsync(s => s with { ManagedAi = true, AiSetupChosen = true });
            if (maintenance || exiting) return;
            preparationTask = services.Preparation.StartAsync(allowDownload);
        }
        catch (Exception ex) { services.Log.Write("ai.start_failed", ex); Ui.Error(ex); }
        finally { aiChanging = false; }
        if (preparationTask is not null) _ = ObservePreparationAsync(preparationTask);
    }
    private async Task ObservePreparationAsync(Task task)
    {
        try { await task; }
        catch (Exception ex) { services.Log.Write("ai.preparation_failed", ex); }
        finally
        {
            if (!maintenance && !exiting && !aiChanging)
            {
                if (CanMonitor) Supervisor.Activate();
                Worker.Start();
            }
        }
    }
    public async Task DisableAiAsync()
    {
        if (maintenance || exiting || aiChanging) return;
        aiChanging = true;
        try { await Supervisor.SuspendAsync(); await CancelChatAsync(); await Worker.StopAsync(); await services.Preparation.DisableAsync(); }
        finally { aiChanging = false; if (!maintenance && !exiting) Worker.Start(); }
    }
    public async Task ConfigureAiAsync(bool managed, string endpoint, string embedding, string chat)
    {
        if (maintenance || exiting || aiChanging || services.Preparation.IsRunning) throw new InvalidOperationException("请先暂停准备，再更改高级设置。");
        var candidate = services.Settings with { ManagedAi = managed, Endpoint = endpoint, EmbeddingModel = embedding, ChatModel = chat, AiSetupChosen = true, AiEnabled = false };
        candidate.Validate();
        await DisableAiAsync();
        if (maintenance || exiting) return;
        await services.UpdateSettingsAsync(s => s with { ManagedAi = managed, Endpoint = endpoint, EmbeddingModel = embedding, ChatModel = chat, AiSetupChosen = true, AiEnabled = false });
    }
    public async Task<string> ChangeAiDirectoryAsync(string parentDirectory)
    {
        if (maintenance || exiting || aiChanging || services.Preparation.IsRunning)
            throw new InvalidOperationException("请先暂停 AI 准备，并等待当前操作结束，再更换保存位置。");
        if (!Path.IsPathFullyQualified(parentDirectory) || parentDirectory.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("请选择本机磁盘上的文件夹。");
        var profile = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(services.DataDirectory).ToUpperInvariant())))[..8];
        var target = Path.GetFullPath(Path.Combine(parentDirectory, "NovelRAG-AI-" + profile));
        if (string.Equals(target, Path.GetFullPath(services.AiBackend.Root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return target;
        // Validate and probe before interrupting the currently working installation.
        var candidate = services.Settings with { AiDirectory = target, ModelsPath = Path.Combine(target, "models") };
        candidate.Validate();
        var probe = ComponentFiles.SafeChild(target, ".write-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        aiChanging = true; maintenance = true;
        try
        {
            await Supervisor.SuspendAsync(); await CancelChatAsync(close: true);
            await services.Preparation.ShutdownAsync();
            await Worker.StopAsync();
            await services.UpdateSettingsAsync(s => s with
            {
                AiDirectory = target,
                ModelsPath = Path.Combine(target, "models"),
                ManagedAi = true,
                AiSetupChosen = true,
                AiEnabled = false
            });
            var state = new AiPreparationStatus(AiStage.Disabled, "保存位置已更换，请点击“一键准备 AI”在新位置准备组件。");
            await services.AiBackend.SaveStateAsync(state, CancellationToken.None);
            services.Preparation.Restore(state);
            return target;
        }
        finally
        {
            services.Preparation.ReleaseMaintenance(); maintenance = false; aiChanging = false; Worker.Start();
        }
    }
    public async Task<bool> PrepareRestoreAsync()
    {
        if (maintenance || exiting) return false;
        maintenance = true;
        try
        {
            // Block tray, pet and IPC navigation before awaiting shutdown or showing confirmations.
            capture?.Close(); if (capture is not null) { maintenance = false; return false; }
            library?.Close(); if (library is not null) { maintenance = false; return false; }
            await Supervisor.SuspendAsync(); await CancelChatAsync(close: true); await services.Preparation.SuspendAsync(); await Worker.StopAsync(); return true;
        }
        catch { maintenance = false; services.Preparation.ReleaseMaintenance(); ResumeMonitoring(); Worker.Start(); throw; }
    }
    public void FinishRestore() { maintenance = false; services.Preparation.ReleaseMaintenance(); ResumeMonitoring(); Worker.Start(); }
    public Task SuspendMonitoringAsync() => Supervisor.SuspendAsync();
    public void ResumeMonitoring() { if (CanMonitor && !recovering) Supervisor.Activate(resetBudget: false); }
    private async Task CancelChatAsync(bool close = false)
    {
        var window = chat; if (window is null) return;
        await window.CancelRequestAsync(); if (close) window.Close();
    }
    private Task<bool> DispatchRuntimeFailureAsync(AiRuntimeFailure failure, CancellationToken ct)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        return dispatcher is not null && !dispatcher.CheckAccess()
            ? dispatcher.InvokeAsync(() => HandleRuntimeFailureAsync(failure, ct), System.Windows.Threading.DispatcherPriority.Normal, ct).Task.Unwrap()
            : HandleRuntimeFailureAsync(failure, ct);
    }
    private async Task<bool> HandleRuntimeFailureAsync(AiRuntimeFailure failure, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); if (maintenance || exiting || aiChanging || !services.Settings.AiEnabled) return false;
        recovering = true;
        try
        {
            services.Preparation.RuntimeUnavailable(failure);
            await CancelChatAsync(); await Worker.StopAsync(); ct.ThrowIfCancellationRequested();
            if (maintenance || exiting || aiChanging || !services.Settings.AiEnabled) return false;
            if (failure.CanRecover)
            {
                await services.Preparation.StartAsync(allowDownload: false, ct); ct.ThrowIfCancellationRequested();
                if (services.Preparation.Status.SearchReady)
                { return true; }
                services.Preparation.RuntimeUnavailable(failure with { CanRecover = false });
            }
            await services.AiBackend.SaveStateAsync(services.Preparation.Status, ct); return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        { services.Log.Write("ai.runtime_recovery_failed", ex); services.Preparation.RuntimeUnavailable(failure with { CanRecover = false }); return false; }
        finally { recovering = false; if (!maintenance && !exiting && !aiChanging && !ct.IsCancellationRequested) Worker.Start(); }
    }
    public async Task ExitAsync()
    {
        if (exiting || maintenance) return; exiting = true;
        var completed = false;
        try
        {
            capture?.Close(); if (capture is not null) return;
            library?.Close(); if (library is not null) return;
            settings?.Close(); if (settings is not null) return;
            await Supervisor.SuspendAsync(); await supervisionStop.CancelAsync(); await supervisionTask;
            await CancelChatAsync(close: true); await services.Preparation.ShutdownAsync(); await Worker.DisposeAsync();
            services.Ideas.Changed -= Worker.Request;
            tray?.Dispose(); Pet.Close(); await Pet.RendererTask; services.Log.Write("app.exiting"); System.Windows.Application.Current.Shutdown(); completed = true;
        }
        finally { if (!completed) exiting = false; }
    }
}
