using System.Windows.Input;

namespace NovelRAG.Desktop;

public sealed class LibraryWindow : Window
{
    private readonly AppServices services;
    private readonly DesktopController controller;
    private readonly ComboBox projects = new() { DisplayMemberPath = "Name", SelectedValuePath = "Id", Padding = new Thickness(8) };
    private readonly ListBox tags = new() { DisplayMemberPath = "Name", SelectionMode = SelectionMode.Multiple, MinHeight = 110, MaxHeight = 300 };
    private readonly TextBox query = Ui.Input("LibrarySearch");
    private readonly CheckBox archived = new() { Content = "包含归档", Margin = new Thickness(4, 10, 12, 4) };
    private readonly ListBox list = new() { DisplayMemberPath = "Preview", BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly IdeaEditor editor = new();
    private readonly TextBlock metadata = Ui.Text("选择一条灵感查看与编辑", 12, Ui.Muted);
    private readonly TextBlock status = Ui.Text("", 12, Ui.Muted);
    private readonly TaskCompletionSource ready = new();
    private readonly Grid root = new() { Margin = new Thickness(22) };
    private Idea? selected;
    private bool selectionGuard, busy;
    private int offset;
    public LibraryWindow(AppServices services, DesktopController controller)
    {
        this.services = services; this.controller = controller; Ui.Configure(this, "灵感库", 1220, 760);
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var actions = Ui.Row(Ui.Button("＋ 记录灵感", () => { controller.OpenCapture(); return Task.CompletedTask; }, true), Ui.Button("搜索 / 提问", () => { controller.OpenChat(); return Task.CompletedTask; }), Ui.Button("设置", () => { controller.OpenSettings(); return Task.CompletedTask; }));
        DockPanel.SetDock(actions, Dock.Right); head.Children.Add(actions); head.Children.Add(Ui.Text("灵感库", 28)); root.Children.Add(head);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(205) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(355) }); body.ColumnDefinitions.Add(new()); Grid.SetRow(body, 1); root.Children.Add(body);
        var side = new StackPanel { Margin = new Thickness(0, 0, 14, 0) }; side.Children.Add(Ui.Text("作品", 17)); side.Children.Add(projects);
        side.Children.Add(Ui.Row(Ui.Button("新建", () => ProjectAction("new")), Ui.Button("改名", () => ProjectAction("rename")))); side.Children.Add(Ui.Button("移除作品…", () => ProjectAction("delete")));
        side.Children.Add(Ui.Text("标签 · 多选需全部匹配", 13, Ui.Muted)); side.Children.Add(tags);
        side.Children.Add(Ui.Row(Ui.Button("新建", () => TagAction("new")), Ui.Button("改名", () => TagAction("rename")))); side.Children.Add(Ui.Button("移除标签…", () => TagAction("delete")));
        side.Children.Add(archived); side.Children.Add(Ui.Button("应用筛选 / 刷新", () => RefreshRequested(true), true));
        side.Children.Add(Ui.Text("移除作品后，灵感会保留在“未分类”。归档内容默认不参与搜索。", 12, Ui.Muted)); body.Children.Add(side);
        var center = new DockPanel { Margin = new Thickness(0, 0, 16, 0) }; Grid.SetColumn(center, 1); body.Children.Add(Ui.Card(center)); Grid.SetColumn(body.Children[^1], 1);
        var search = new StackPanel(); search.Children.Add(Ui.Text("关键词检索", 17)); search.Children.Add(query); search.Children.Add(Ui.Button("搜索", () => RefreshRequested(true)));
        DockPanel.SetDock(search, Dock.Top); center.Children.Add(search);
        var pager = new StackPanel(); pager.Children.Add(status); pager.Children.Add(Ui.Row(Ui.Button("上一页", () => Page(-50)), Ui.Button("下一页", () => Page(50)))); DockPanel.SetDock(pager, Dock.Bottom); center.Children.Add(pager); center.Children.Add(list);
        var detail = new DockPanel(); var detailCard = Ui.Card(detail); Grid.SetColumn(detailCard, 2); body.Children.Add(detailCard);
        DockPanel.SetDock(metadata, Dock.Top); detail.Children.Add(metadata);
        var detailButtons = new WrapPanel();
        foreach (var b in new[] { Ui.Button("保存修改", () => RunAsync(SaveAsync), true), Ui.Button("归档 / 取消归档", () => RunAsync(ArchiveAsync)), Ui.Button("永久删除…", () => RunAsync(DeleteAsync)) }) detailButtons.Children.Add(b);
        DockPanel.SetDock(detailButtons, Dock.Bottom); detail.Children.Add(detailButtons); detail.Children.Add(editor); editor.IsEnabled = false;
        Content = root;
        Loaded += async (_, _) => { try { await RunAsync(async () => { await LoadFilters(); await RefreshAsync(); }); ready.TrySetResult(); } catch (Exception ex) { ready.TrySetException(ex); Ui.Error(ex); } };
        list.SelectionChanged += async (_, _) =>
        {
            if (selectionGuard || busy || list.SelectedItem is not Idea next || next.Id == selected?.Id) return;
            if (!DiscardChanges()) { selectionGuard = true; list.SelectedItem = selected; selectionGuard = false; return; }
            try { await RunAsync(() => SelectAsync(next)); } catch (Exception ex) { Ui.Error(ex); }
        };
        query.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { try { await RefreshRequested(true); } catch (Exception ex) { Ui.Error(ex); } } };
        Closing += (_, e) => { if (busy || !DiscardChanges()) e.Cancel = true; };
    }
    private bool DiscardChanges() => !editor.Dirty || Ui.Confirm("放弃当前灵感尚未保存的修改？");
    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return; busy = true; root.IsEnabled = false;
        try { await action(); } finally { root.IsEnabled = true; busy = false; }
    }
    private async Task LoadFilters()
    {
        var projectId = projects.SelectedValue; var selectedTags = tags.SelectedItems.Cast<Tag>().Select(t => t.Id).ToHashSet();
        projects.ItemsSource = new[] { new Project(-1, "全部作品"), new Project(0, "未分类") }.Concat(await services.Projects.ListAsync()).ToArray();
        projects.SelectedValue = projectId ?? -1L; if (projects.SelectedIndex < 0) projects.SelectedIndex = 0;
        tags.ItemsSource = await services.Tags.ListAsync(); foreach (Tag tag in tags.Items) if (selectedTags.Contains(tag.Id)) tags.SelectedItems.Add(tag);
    }
    private RetrievalQuery Query()
    {
        var p = projects.SelectedValue is long id ? id : -1;
        return new(query.Text, p > 0 ? p : null, tags.SelectedItems.Cast<Tag>().Select(t => t.Id).ToArray(), archived.IsChecked == true, p == 0, Limit: 50, Offset: offset);
    }
    private async Task RefreshRequested(bool reset)
    {
        if (!DiscardChanges()) return;
        await RunAsync(async () => { if (reset) offset = 0; await LoadFilters(); await RefreshAsync(); });
    }
    private async Task Page(int delta) { if (!DiscardChanges()) return; await RunAsync(async () => { offset = Math.Max(0, offset + delta); await RefreshAsync(); }); }
    private async Task RefreshAsync()
    {
        var results = await services.Search.SearchAsync(Query());
        selectionGuard = true; list.ItemsSource = results.Results.Select(r => r.Idea).ToArray(); list.SelectedIndex = -1; selectionGuard = false;
        selected = null; await editor.LoadAsync(services); editor.IsEnabled = false; metadata.Text = "选择一条灵感查看与编辑";
        status.Text = results.Results.Count == 0 ? "没有匹配记录" : $"第 {offset / 50 + 1} 页 · {results.Results.Count} 条";
    }
    private async Task SelectAsync(Idea idea)
    {
        var current = await services.Ideas.GetAsync(idea.Id);
        if (current is null) { status.Text = "此灵感已被删除，请刷新。"; return; }
        selected = current; await editor.LoadAsync(services, current); editor.IsEnabled = true;
        metadata.Text = $"#{current.Id} · {(current.Archived ? "已归档" : "灵感")}\n创建 {current.CreatedAt.LocalDateTime:g} · 更新 {current.UpdatedAt.LocalDateTime:g}";
    }
    public async Task OpenIdeaAsync(long id)
    {
        try
        {
            await ready.Task; if (!DiscardChanges()) return;
            await RunAsync(async () => { var idea = await services.Ideas.GetAsync(id); if (idea is null) { status.Text = "来源已被删除。"; return; } await SelectAsync(idea); }); Activate();
        }
        catch (Exception ex) { Ui.Error(ex); }
    }
    private async Task SaveAsync()
    {
        if (selected is null) return;
        selected = await services.Ideas.SaveAsync(editor.Draft(), selected.Id); editor.MarkClean(); var id = selected.Id;
        await LoadFilters(); await RefreshAsync(); await SelectAsync((await services.Ideas.GetAsync(id))!); status.Text = "修改已保存";
    }
    private async Task ArchiveAsync()
    {
        if (selected is null || !DiscardChanges()) return;
        await services.Ideas.ArchiveAsync(selected.Id, !selected.Archived); await RefreshAsync();
    }
    private async Task DeleteAsync()
    {
        if (selected is null || !Ui.Confirm("永久删除此灵感？此操作无法撤销。建议优先使用归档。")) return;
        await services.Ideas.DeleteAsync(selected.Id); editor.MarkClean(); await RefreshAsync();
    }
    private Task ProjectAction(string action) => RunAsync(async () =>
    {
        if (!DiscardChanges()) return;
        var item = projects.SelectedItem as Project;
        if (action != "new" && (item is null || item.Id <= 0)) throw new ArgumentException("请先选择一个具体作品。");
        if (action == "delete") { if (!Ui.Confirm("移除该作品？其中灵感将保留在“未分类”。")) return; await services.Projects.DeleteAsync(item!.Id); }
        else { var name = Ui.Prompt(this, action == "new" ? "新建作品" : "作品改名", action == "new" ? "" : item!.Name); if (name is null) return; await services.Projects.SaveAsync(name, action == "new" ? null : item!.Id); }
        await LoadFilters(); await RefreshAsync();
    });
    private Task TagAction(string action) => RunAsync(async () =>
    {
        if (!DiscardChanges()) return;
        var selectedTags = tags.SelectedItems.Cast<Tag>().ToArray();
        if (action != "new" && selectedTags.Length != 1) throw new ArgumentException("请只选择一个标签进行管理。");
        var item = selectedTags.FirstOrDefault();
        if (action == "delete") { if (!Ui.Confirm("移除此标签及其关联？灵感内容将保留。")) return; await services.Tags.DeleteAsync(item!.Id); }
        else { var name = Ui.Prompt(this, action == "new" ? "新建标签" : "标签改名", action == "new" ? "" : item!.Name); if (name is null) return; await services.Tags.SaveAsync(name, action == "new" ? null : item!.Id); }
        await LoadFilters(); await RefreshAsync();
    });
}
