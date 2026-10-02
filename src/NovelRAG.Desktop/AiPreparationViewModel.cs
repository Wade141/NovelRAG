using System.ComponentModel;
using System.Windows.Threading;
using NovelRAG.Infrastructure;

namespace NovelRAG.Desktop;

public sealed class AiPreparationViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppServices services;
    private readonly DispatcherTimer timer;
    private bool disposed;
    public AiPreflight? Estimate { get; private set; }
    public string EstimateText { get; private set; } = "正在检查电脑和所需空间…";
    public AiPreparationStatus State => services.Preparation.Status;
    public bool Working => services.Preparation.IsRunning;
    public Visibility ProgressVisibility => Working ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TransferVisibility => string.IsNullOrEmpty(TransferText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TryVisibility => State.SearchReady ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DisableVisibility => services.Settings.AiEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ChooseVisibility => CanChooseDirectory && Estimate?.CanPrepare != true ? Visibility.Visible : Visibility.Collapsed;
    public bool CanStart => !Working && Estimate?.Supported == true;
    public bool CanChooseDirectory => !Working;
    public string DirectoryHint => Working ? "正在准备组件，请先暂停，再更换保存位置。" : "可选择其他磁盘；将在所选文件夹内创建专用子目录。更换后请在新位置准备 AI，旧目录会保留。";
    public string StatusText => State.Message;
    public string Capabilities => $"智能搜索：{(State.SearchReady ? "可用" : "尚未就绪")}　　本地问答：{(State.ChatReady ? "可用" : "尚未就绪")}" + (State.Pending > 0 ? $"\n仍有 {State.Pending} 条灵感等待整理，失败 {State.Failed} 条" : "");
    public string StartLabel => State.Stage switch { AiStage.Ready => "检查与修复", AiStage.Paused or AiStage.Partial or AiStage.Failed => "继续准备", _ => "一键准备 AI" };
    public string Details => State.Detail ?? "暂无错误。";
    public string TransferText => State.Transfer is not { } p ? "" : p.Message + (p.Total > 0 ? $"\n{Size(p.Completed)} / {Size(p.Total)}" + (p.BytesPerSecond > 0 ? $" · {Size((long)p.BytesPerSecond)}/秒" : "") : "");
    public bool Indeterminate => Working && State.Transfer?.Total is not > 0;
    public double Percent => State.Transfer is { Total: > 0 } p ? Math.Clamp(p.Completed * 100d / p.Total, 0, 100) : 0;
    public string DirectoryText => services.AiBackend.Root;
    public string ModeText => services.Settings.ManagedAi || !services.Settings.AiSetupChosen && !services.Settings.AiEnabled ? "自动管理本地 AI" : "使用已有本机服务";
    public event PropertyChangedEventHandler? PropertyChanged;
    public AiPreparationViewModel(AppServices services)
    {
        this.services = services;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => Refresh(); timer.Start();
    }
    public async Task RefreshEstimateAsync()
    {
        try
        {
            var estimate = await Task.Run(services.AiBackend.Inspect);
            if (disposed) return;
            Estimate = estimate;
            EstimateText = estimate.InstalledBytes == 0 ? estimate.Message : $"预计下载 {Size(estimate.DownloadBytes)} · 安装后约 {Size(estimate.InstalledBytes)}\n准备时还需约 {Size(estimate.AdditionalBytes)} 空间，当前可用 {Size(estimate.FreeBytes)}\n{estimate.Message}";
        }
        catch (Exception ex) { if (disposed) return; Estimate = null; EstimateText = "无法检查组件目录，请选择其他磁盘。"; services.Log.Write("ai.preflight_failed", ex); }
        Refresh();
    }
    private void Refresh() { if (!disposed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    public static string Size(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GB" : $"{bytes / (1024d * 1024):0.0} MB";
    public void Dispose() { disposed = true; timer.Stop(); }
}
