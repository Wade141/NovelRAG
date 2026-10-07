using System.Windows.Input;
using System.Windows.Automation;

namespace NovelRAG.Desktop;

public sealed class ChatWindow : Window
{
    private readonly AppServices services;
    private readonly DesktopController controller;
    private readonly TextBox question = Ui.Input("Question", true);
    private readonly TextBox answer = Ui.Input("Answer", true);
    private readonly DateRangeControl dates = new();
    private readonly ComboBox projects = new() { DisplayMemberPath = "Name", SelectedValuePath = "Id", MinWidth = 200, Padding = new Thickness(6), Margin = new Thickness(0, 4, 12, 4) };
    private readonly ListBox sources = new() { DisplayMemberPath = "Preview", MinHeight = 90, MaxHeight = 200 };
    private readonly TextBlock status = Ui.Text("关键词搜索随时可用；首次使用智能功能可点击准备 AI。", 12, Ui.Muted);
    private readonly StackPanel buttons;
    private CancellationTokenSource? request;
    private TaskCompletionSource? requestFinished;
    public Task CancelRequestAsync()
    {
        var finished = requestFinished?.Task ?? Task.CompletedTask; request?.Cancel(); return finished;
    }
    public ChatWindow(AppServices services, DesktopController controller)
    {
        this.services = services; this.controller = controller; Ui.Configure(this, "搜索 / 提问", 820, 760);
        var root = new DockPanel { Margin = new Thickness(24) }; var header = new StackPanel(); header.Children.Add(Ui.Text("找回曾经的念头", 26));
        header.Children.Add(Ui.Text("例如：我是否安排过女主和男主在童年相遇？", 13, Ui.Muted)); question.Height = 95; header.Children.Add(question);
        buttons = Ui.Row(Ui.Button("关键词", () => SearchAsync("keyword")), Ui.Button("语义搜索", () => SearchAsync("semantic")), Ui.Button("混合搜索", () => SearchAsync("hybrid")), Ui.Button("提问", () => SearchAsync("chat"), true));
        header.Children.Add(projects); header.Children.Add(dates); header.Children.Add(buttons); header.Children.Add(Ui.Row(Ui.Button("准备 AI / 查看进度", () => { controller.OpenSettings(); return Task.CompletedTask; }), Ui.Button("取消当前请求", () => { request?.Cancel(); return Task.CompletedTask; }))); header.Children.Add(status);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var bottom = new StackPanel(); bottom.Children.Add(Ui.Text("来源 · 双击查看原始灵感", 16)); bottom.Children.Add(sources); bottom.Children.Add(Ui.Button("打开选中来源", () => { OpenSource(); return Task.CompletedTask; }));
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); answer.IsReadOnly = true; root.Children.Add(answer); Content = root;
        sources.MouseDoubleClick += (_, _) => OpenSource();
        AutomationProperties.SetAutomationId(sources, "SearchSources");
        Loaded += async (_, _) => { try { projects.ItemsSource = new[] { new Project(-1, "全部作品"), new Project(0, "未分类") }.Concat(await services.Projects.ListAsync()).ToArray(); projects.SelectedIndex = 0; question.Focus(); } catch (Exception ex) { Ui.Error(ex); } };
        Closed += (_, _) => request?.Cancel();
        question.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; try { await SearchAsync("chat"); } catch (Exception ex) { Ui.Error(ex); } } };
    }
    private void OpenSource() { if (sources.SelectedItem is Idea idea) controller.OpenLibrary(idea.Id); }
    private async Task SearchAsync(string mode)
    {
        if (request is not null) return;
        if (mode != "keyword" && controller.AiTransitioning) { status.Text = "AI 正在切换或恢复，请稍后重试；关键词搜索仍可用。"; return; }
        if (string.IsNullOrWhiteSpace(question.Text)) throw new ArgumentException("请输入要查找的内容。");
        if (!dates.TryGetUtcBounds(out var from, out var to)) return;
        var p = projects.SelectedValue is long id ? id : -1;
        var q = new RetrievalQuery(question.Text, p > 0 ? p : null, UncategorizedOnly: p == 0, From: from, To: to, Limit: services.Settings.Retrieval.SearchTopK);
        if (mode == "semantic" && !services.Preparation.Status.SearchReady || mode == "chat" && !services.Preparation.Status.ChatReady)
        { status.Text = "此功能尚未就绪，已打开 AI 准备页面；关键词搜索随时可用。"; controller.OpenSettings(); return; }
        using var cts = new CancellationTokenSource(); request = cts; var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); requestFinished = finished;
        buttons.IsEnabled = false; question.IsEnabled = false; projects.IsEnabled = false; dates.IsEnabled = false;
        answer.Text = ""; sources.ItemsSource = null; status.Text = "正在检索…";
        try
        {
            if (mode == "chat")
            {
                var result = await services.Chat.AskAsync(q, cts.Token); cts.Token.ThrowIfCancellationRequested();
                answer.Text = result.Text; sources.ItemsSource = result.Sources.Select(r => r.Idea).ToArray(); status.Text = result.IsFallback ? "已显示证据或降级结果" : "回答已生成 · 请核对原文";
            }
            else
            {
                var result = await services.Search.SearchAsync(q, mode, cts.Token); cts.Token.ThrowIfCancellationRequested();
                answer.Text = result.Results.Count == 0 ? "没有找到匹配的灵感。" : string.Join("\n\n", result.Results.Select(r => $"[#{r.Idea.Id}] {r.Excerpt}"));
                sources.ItemsSource = result.Results.Select(r => r.Idea).ToArray(); status.Text = result.Warning ?? $"找到 {result.Results.Count} 条灵感";
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { status.Text = "已取消"; }
        catch (Exception ex) { services.Log.Write("search.failed", ex); status.Text = "检索未完成，关键词搜索仍可用。可在设置中点击检查与修复。"; throw; }
        finally { request = null; buttons.IsEnabled = true; question.IsEnabled = true; projects.IsEnabled = true; dates.IsEnabled = true; requestFinished = null; finished.TrySetResult(); }
    }
}
