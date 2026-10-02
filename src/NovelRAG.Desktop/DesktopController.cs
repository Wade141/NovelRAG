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
    private bool exiting;
    private bool maintenance;
    private bool aiChanging;
    public PetWindow Pet { get; private set; } = null!;
    public IndexWorker Worker { get; } = new(services);
    public void Start()
    {
        Pet = new(services, this, new OwlPetRenderer());
        if (services.Settings.PetVisible) Pet.Show();
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "NovelRAG · 灵感随手记", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        void Add(string name, Action action) => menu.Items.Add(name, null, (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(action));
        Add("记录灵感", OpenCapture); Add("搜索 / 提问", OpenChat); Add("灵感库", () => OpenLibrary());
        Add("显示 / 隐藏桌宠", () => _ = TogglePetAsync()); Add("设置", OpenSettings); menu.Items.Add(new Forms.ToolStripSeparator()); Add("退出", () => _ = ExitAsync());
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => OpenCapture();
        services.Ideas.Changed += Worker.Request; Worker.Start();
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
        try { await services.UpdateSettingsAsync(s => s with { PetVisible = !s.PetVisible }); Pet.ApplySettings(); }
        catch (Exception ex) { Ui.Error(ex); }
    }
    public void ApplySettings() { Pet.ApplySettings(); Worker.Request(); }
    public async Task StartAiPreparationAsync(bool allowDownload = true)
    {
        if (maintenance || exiting || aiChanging || services.Preparation.IsRunning) return;
        aiChanging = true;
        try
        {
            await Worker.StopAsync();
            if (maintenance || exiting) return;
            if (!services.Settings.AiSetupChosen && !services.Settings.AiEnabled)
                await services.UpdateSettingsAsync(s => s with { ManagedAi = true, AiSetupChosen = true });
            if (maintenance || exiting) return;
            _ = ObservePreparationAsync(services.Preparation.StartAsync(allowDownload));
        }
        catch (Exception ex) { services.Log.Write("ai.start_failed", ex); Ui.Error(ex); }
        finally { aiChanging = false; }
    }
    private async Task ObservePreparationAsync(Task task)
    {
        try { await task; }
        catch (Exception ex) { services.Log.Write("ai.preparation_failed", ex); }
        finally { if (!maintenance && !exiting) Worker.Start(); }
    }
    public async Task DisableAiAsync()
    {
        if (maintenance || exiting || aiChanging) return;
        aiChanging = true;
        try { await Worker.StopAsync(); await services.Preparation.DisableAsync(); }
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
            chat?.Close();
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
            chat?.Close(); await services.Preparation.SuspendAsync(); await Worker.StopAsync(); return true;
        }
        catch { maintenance = false; services.Preparation.ReleaseMaintenance(); Worker.Start(); throw; }
    }
    public void FinishRestore() { maintenance = false; services.Preparation.ReleaseMaintenance(); Worker.Start(); }
    public async Task ExitAsync()
    {
        if (exiting || maintenance) return; exiting = true;
        try
        {
            capture?.Close(); if (capture is not null) return;
            library?.Close(); if (library is not null) return;
            settings?.Close(); if (settings is not null) return;
            chat?.Close(); await services.Preparation.ShutdownAsync(); await Worker.DisposeAsync();
            services.Ideas.Changed -= Worker.Request;
            tray?.Dispose(); Pet.Close(); services.Log.Write("app.exiting"); System.Windows.Application.Current.Shutdown();
        }
        finally { exiting = false; }
    }
}
