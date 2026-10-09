using System.Windows.Input;

namespace NovelRAG.Desktop;

public sealed class CaptureWindow : Window
{
    private readonly AppServices services;
    private readonly IdeaEditor editor;
    private readonly TextBlock status = Ui.Text("Ctrl+Enter 保存 · Esc 取消", 12, Ui.Muted);
    private readonly Button save;
    private bool saving;
    public CaptureWindow(AppServices services)
    {
        this.services = services; editor = new(CreateProjectAsync); Ui.Configure(this, "记录灵感", 520, 600); Topmost = true;
        var panel = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel(); header.Children.Add(Ui.Text("留住这一刻的灵感", 24)); header.Children.Add(Ui.Text("一段对白、一个转折，或尚未成形的念头。", 12, Ui.Muted)); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        save = Ui.Button("保存灵感", SaveAsync, true); save.IsEnabled = false;
        var footer = new StackPanel(); footer.Children.Add(status); footer.Children.Add(Ui.Row(save, Ui.Button("取消", () => { Close(); return Task.CompletedTask; })));
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer); panel.Children.Add(editor); Content = panel;
        Loaded += async (_, _) => { try { await editor.LoadAsync(services, preferredProject: services.Settings.LastProjectId); editor.ContentInput.Focus(); save.IsEnabled = true; } catch (Exception ex) { Ui.Error(ex); } };
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; try { await SaveAsync(); } catch (Exception ex) { Ui.Error(ex); } } };
        Closing += (_, e) => { if (saving || editor.Dirty && !Ui.Confirm("放弃尚未保存的灵感？")) e.Cancel = true; };
    }
    private async Task CreateProjectAsync()
    {
        if (saving || !editor.Ready) return;
        saving = true; editor.IsEnabled = false; save.IsEnabled = false;
        try
        {
            var name = Ui.Prompt(this, "新建作品"); if (name is null) return;
            var project = await services.Projects.SaveAsync(name);
            var projects = await services.Projects.ListAsync();
            // Refresh only the selector: reloading the editor would discard the pending draft.
            editor.ProjectInput.ItemsSource = new[] { new Project(0, "未分类") }.Concat(projects).ToArray();
            editor.ProjectInput.SelectedValue = project.Id;
        }
        finally { saving = false; editor.IsEnabled = true; save.IsEnabled = true; editor.ContentInput.Focus(); }
    }
    private async Task SaveAsync()
    {
        if (saving || !editor.Ready) return; saving = true; editor.IsEnabled = false; save.IsEnabled = false;
        try
        {
            var idea = await services.Ideas.SaveAsync(editor.Draft()); editor.MarkClean();
            try { await services.UpdateSettingsAsync(s => s with { LastProjectId = idea.ProjectId }); } catch (Exception ex) { services.Log.Write("capture.preference_failed", ex); }
            saving = false; Close();
        }
        catch { status.Text = "未保存，内容仍保留。请修正问题后重试。"; throw; }
        finally { saving = false; editor.IsEnabled = true; save.IsEnabled = true; }
    }
}
