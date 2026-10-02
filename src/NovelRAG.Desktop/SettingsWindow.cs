using Microsoft.Win32;
using NovelRAG.Infrastructure;
using System.Windows.Automation;
using System.Windows.Data;

namespace NovelRAG.Desktop;

public sealed class SettingsWindow : Window
{
    private readonly AppServices services;
    private readonly DesktopController controller;
    private readonly CheckBox visible = new() { Content = "显示桌宠", Margin = new Thickness(0, 8, 0, 8) };
    private readonly CheckBox snap = new() { Content = "靠近屏幕边缘时吸附", Margin = new Thickness(0, 8, 0, 8) };
    private readonly AiPreparationViewModel preparation;
    private readonly Slider scale = new() { Minimum = 0.6, Maximum = 2, TickFrequency = 0.1, IsSnapToTickEnabled = true, Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox endpoint = Ui.Input("OllamaEndpoint");
    private readonly TextBox embedding = Ui.Input("EmbeddingModel");
    private readonly TextBox model = Ui.Input("ChatModel");
    private readonly TextBlock status = Ui.Text("", 12, Ui.Muted);
    private readonly StackPanel panel = new() { Margin = new Thickness(26) };
    private CancellationTokenSource? operation;
    private bool busy;
    private readonly Button cancelOperation;
    public SettingsWindow(AppServices services, DesktopController controller)
    {
        this.services = services; this.controller = controller; preparation = new(services); Ui.Configure(this, "设置", 760, 850);
        var s = services.Settings; visible.IsChecked = s.PetVisible; snap.IsChecked = s.EdgeSnap; scale.Value = s.PetScale;
        endpoint.Text = s.Endpoint; embedding.Text = s.EmbeddingModel; model.Text = s.ChatModel;
        panel.Children.Add(Ui.Text("设置", 28));
        var localAi = new StackPanel(); localAi.Children.Add(Ui.Text("本地 AI", 23));
        localAi.Children.Add(Ui.Text("用自己的灵感进行智能搜索和问答。准备完成后可离线使用。", 14));
        TextBlock BoundText(string property, double size = 12)
        { var text = Ui.Text("", size, Ui.Muted); text.SetBinding(TextBlock.TextProperty, new Binding(property) { Source = preparation }); return text; }
        localAi.Children.Add(BoundText(nameof(preparation.EstimateText)));
        localAi.Children.Add(BoundText(nameof(preparation.Capabilities), 14));
        localAi.Children.Add(BoundText(nameof(preparation.StatusText), 14));
        var progress = new ProgressBar { Height = 7, Margin = new Thickness(0, 5, 0, 8), Maximum = 100 };
        progress.SetBinding(ProgressBar.ValueProperty, new Binding(nameof(preparation.Percent)) { Source = preparation, Mode = BindingMode.OneWay });
        progress.SetBinding(ProgressBar.IsIndeterminateProperty, new Binding(nameof(preparation.Indeterminate)) { Source = preparation }); localAi.Children.Add(progress);
        progress.SetBinding(VisibilityProperty, new Binding(nameof(preparation.ProgressVisibility)) { Source = preparation });
        var transfer = BoundText(nameof(preparation.TransferText)); transfer.SetBinding(VisibilityProperty, new Binding(nameof(preparation.TransferVisibility)) { Source = preparation }); localAi.Children.Add(transfer);
        var prepare = Ui.Button("一键准备 AI", () => controller.StartAiPreparationAsync(), true); AutomationProperties.SetAutomationId(prepare, "PrepareAi");
        prepare.SetBinding(ContentControl.ContentProperty, new Binding(nameof(preparation.StartLabel)) { Source = preparation });
        prepare.SetBinding(IsEnabledProperty, new Binding(nameof(preparation.CanStart)) { Source = preparation });
        var pause = Ui.Button("暂停", () => services.Preparation.PauseAsync()); pause.SetBinding(IsEnabledProperty, new Binding(nameof(preparation.Working)) { Source = preparation });
        pause.SetBinding(VisibilityProperty, new Binding(nameof(preparation.ProgressVisibility)) { Source = preparation });
        var tryAi = Ui.Button("去试一试", () => { controller.OpenChat(); return Task.CompletedTask; });
        tryAi.SetBinding(VisibilityProperty, new Binding(nameof(preparation.TryVisibility)) { Source = preparation });
        var disable = Ui.Button("关闭 AI", () => controller.DisableAiAsync()); disable.SetBinding(VisibilityProperty, new Binding(nameof(preparation.DisableVisibility)) { Source = preparation });
        localAi.Children.Add(Ui.Row(prepare, pause, tryAi, disable));
        localAi.Children.Add(Ui.Text("准备时可继续记录灵感。关闭此页会在托盘继续；退出应用后，下次可继续准备。", 12, Ui.Muted));
        localAi.Children.Add(Ui.Text("组件保存位置", 14));
        localAi.Children.Add(BoundText(nameof(preparation.DirectoryText)));
        var choose = Ui.Button("选择组件保存位置…", ChooseDirectoryAsync); choose.SetBinding(IsEnabledProperty, new Binding(nameof(preparation.CanChooseDirectory)) { Source = preparation }); localAi.Children.Add(choose);
        AutomationProperties.SetAutomationId(choose, "ChooseAiDirectory");
        localAi.Children.Add(BoundText(nameof(preparation.DirectoryHint)));
        var recheck = Ui.Button("重新检查空间", () => preparation.RefreshEstimateAsync());
        recheck.SetBinding(VisibilityProperty, new Binding(nameof(preparation.ChooseVisibility)) { Source = preparation }); localAi.Children.Add(recheck);
        var advanced = new StackPanel(); advanced.Children.Add(BoundText(nameof(preparation.ModeText), 14));
        advanced.Children.Add(Ui.Text("自动管理模式已包含模型与索引配置。以下选项仅用于连接你自己维护的本机服务。", 12, Ui.Muted));
        void AdvancedField(string title, TextBox input) { advanced.Children.Add(Ui.Text(title, 12, Ui.Muted)); advanced.Children.Add(input); }
        AdvancedField("已有服务地址（仅本机）", endpoint); AdvancedField("嵌入模型", embedding); AdvancedField("问答模型", model);
        advanced.Children.Add(Ui.Row(Ui.Button("使用以上已有服务", UseExternalAsync), Ui.Button("改用自动准备", UseManagedAsync)));
        advanced.Children.Add(Ui.Button("重建语义索引", () => RunAsync(RebuildAsync))); advanced.Children.Add(BoundText(nameof(preparation.Details)));
        localAi.Children.Add(new Expander { Header = "高级设置与诊断", Content = advanced, IsExpanded = false, Margin = new Thickness(0, 12, 0, 4) });
        panel.Children.Add(Ui.Card(localAi));
        panel.Children.Add(Ui.Text("桌宠", 19)); panel.Children.Add(Ui.Row(visible, snap)); panel.Children.Add(Ui.Text("缩放 · 0.6–2 倍", 12, Ui.Muted)); panel.Children.Add(scale);
        panel.Children.Add(Ui.Button("保存桌宠设置", () => RunAsync(SaveAsync)));
        var dataPanel = new StackPanel();
        var location = Ui.Input("DatabasePath"); location.Text = services.Data.DatabasePath; location.IsReadOnly = true; dataPanel.Children.Add(location);
        dataPanel.Children.Add(Ui.Row(Ui.Button("导出 JSON", () => RunAsync(() => ExportAsync(false))), Ui.Button("导出 Markdown", () => RunAsync(() => ExportAsync(true)))));
        dataPanel.Children.Add(Ui.Row(Ui.Button("备份数据库", () => RunAsync(BackupAsync)), Ui.Button("从备份恢复…", () => RunAsync(RestoreAsync))));
        dataPanel.Children.Add(Ui.Text("导出包含归档记录。恢复前会校验备份并保存一份当前数据库副本。", 12, Ui.Muted));
        panel.Children.Add(new Expander { Header = "数据与备份", Content = dataPanel, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(status);
        var outer = new DockPanel();
        cancelOperation = Ui.Button("取消当前索引 / 操作", () => { operation?.Cancel(); return Task.CompletedTask; }); cancelOperation.Visibility = Visibility.Collapsed; cancelOperation.Margin = new Thickness(26, 4, 26, 14); DockPanel.SetDock(cancelOperation, Dock.Bottom); outer.Children.Add(cancelOperation);
        outer.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = outer;
        Closing += (_, e) => { if (busy) { operation?.Cancel(); e.Cancel = true; status.Text = "正在取消，请待操作结束后关闭。"; } };
        Loaded += async (_, _) => await preparation.RefreshEstimateAsync();
        Closed += (_, _) => preparation.Dispose();
    }
    private async Task ChooseDirectoryAsync()
    {
        if (!preparation.CanChooseDirectory) return;
        var dialog = new OpenFolderDialog { Title = "选择保存 AI 组件的位置" };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync(async () =>
        {
            var target = await controller.ChangeAiDirectoryAsync(dialog.FolderName);
            status.Text = "保存位置：" + target + "\n请点击“一键准备 AI”。旧目录保留，原有灵感不受影响。";
            await preparation.RefreshEstimateAsync();
        }, cancellable: false);
    }
    private async Task UseExternalAsync()
    {
        await controller.ConfigureAiAsync(false, endpoint.Text.Trim().TrimEnd('/'), embedding.Text.Trim(), model.Text.Trim());
        await preparation.RefreshEstimateAsync(); await controller.StartAiPreparationAsync(false);
    }
    private async Task UseManagedAsync()
    {
        await controller.ConfigureAiAsync(true, services.Settings.Endpoint, services.Settings.EmbeddingModel, services.Settings.ChatModel);
        await preparation.RefreshEstimateAsync();
    }
    private async Task RunAsync(Func<Task> action, bool cancellable = true)
    {
        if (busy) return; busy = true; panel.IsEnabled = false; cancelOperation.Visibility = cancellable ? Visibility.Visible : Visibility.Collapsed; using var cts = new CancellationTokenSource(); operation = cts;
        try { await action(); }
        catch (OperationCanceledException) { status.Text = "已取消；已保存的灵感不受影响。"; }
        catch (Exception ex) { services.Log.Write("settings.operation_failed", ex); status.Text = "操作未完成。"; throw; }
        finally { busy = false; operation = null; panel.IsEnabled = true; cancelOperation.Visibility = Visibility.Collapsed; }
    }
    private async Task SaveAsync()
    {
        await services.UpdateSettingsAsync(s => s with
        {
            PetVisible = visible.IsChecked == true,
            EdgeSnap = snap.IsChecked == true,
            PetScale = scale.Value
        });
        controller.ApplySettings(); status.Text = "设置已保存。";
    }
    private async Task RebuildAsync()
    {
        if (services.Preparation.IsRunning || !services.Preparation.Status.SearchReady) throw new InvalidOperationException("请先完成智能搜索准备。");
        await services.Preparation.SuspendAsync(); await controller.Worker.StopAsync();
        try
        {
            var result = await services.Indexer.RebuildAsync(new Progress<IndexProgress>(p => status.Text = $"正在建立索引 {p.Completed}/{p.Total} · 失败 {p.Failed}"), operation!.Token);
            status.Text = $"索引处理完成：{result.Completed}/{result.Total}，失败 {result.Failed}。";
            services.Preparation.UpdateIndex(result);
        }
        finally { services.Preparation.ReleaseMaintenance(); controller.Worker.Start(); }
    }
    private string? SavePath(string extension, string filter)
    {
        var dialog = new SaveFileDialog { FileName = "NovelRAG-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + extension, Filter = filter, AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
    private async Task ExportAsync(bool markdown)
    {
        var path = SavePath(markdown ? ".md" : ".json", markdown ? "Markdown|*.md" : "JSON|*.json"); if (path is null) return;
        await services.Data.ExportAsync(markdown ? new MarkdownExporter() : new JsonExporter(), path, operation!.Token); status.Text = "已导出：" + path;
    }
    private async Task BackupAsync()
    {
        var path = SavePath(".db", "SQLite 数据库|*.db"); if (path is null) return;
        await services.Data.BackupAsync(path, operation!.Token); status.Text = "备份已保存：" + path;
    }
    private async Task RestoreAsync()
    {
        var dialog = new OpenFileDialog { Filter = "SQLite 备份|*.db;*.bak|所有文件|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || !Ui.Confirm("恢复会用备份替换当前灵感库。将先保存当前数据库副本。继续？")) return;
        if (!await controller.PrepareRestoreAsync()) { status.Text = "请先保存或关闭正在编辑的窗口。"; return; }
        try
        {
            var path = await services.Data.RestoreAsync(dialog.FileName, operation!.Token);
            status.Text = "恢复成功。恢复前数据保存在：" + path;
        }
        finally { controller.FinishRestore(); }
    }
}
