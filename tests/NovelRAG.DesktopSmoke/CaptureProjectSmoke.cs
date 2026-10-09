using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NovelRAG.Desktop;
using NovelRAG.Domain;
using static NovelRAG.DesktopSmoke.DesktopPerformance;

namespace NovelRAG.DesktopSmoke;

internal static class CaptureProjectSmoke
{
    public static async Task RunAsync(string root)
    {
        using var services = new AppServices(root); await services.InitializeAsync();
        var original = await services.Projects.SaveAsync("已有作品");
        await services.UpdateSettingsAsync(s => s with { LastProjectId = original.Id });
        var capture = new CaptureWindow(services); capture.Show();
        await Until(() => Find<TextBox>(capture, "IdeaContent").IsEnabled);
        var create = Descendants(capture).OfType<Button>().SingleOrDefault(b => Equals(b.Content, "新建")) ?? throw new Exception("Capture is missing the new-project button.");
        var projects = Descendants(capture).OfType<ComboBox>().Single();
        capture.UpdateLayout();
        if (create.TranslatePoint(new Point(), capture).Y < projects.TranslatePoint(new Point(0, projects.ActualHeight), capture).Y) throw new Exception("New project button is not below the selector.");
        var content = Find<TextBox>(capture, "IdeaContent"); var tags = Find<TextBox>(capture, "IdeaTags");
        content.Text = "铜铃属于新作品，保留尚未保存的正文。"; tags.Text = "伏笔，人物";
        void Prompt(string? name)
        {
            Exception? error = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) =>
            {
                var prompt = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Owner == capture);
                if (prompt is null) return;
                timer.Stop();
                try { if (name is null) prompt.Close(); else { Find<TextBox>(prompt, "Name").Text = name; Click(prompt, "确定"); } }
                catch (Exception ex) { error = ex; prompt.Close(); }
            };
            timer.Start();
            try { create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { timer.Stop(); }
            if (error is not null) throw error;
        }
        Prompt(null);
        if ((long)projects.SelectedValue != original.Id || content.Text != "铜铃属于新作品，保留尚未保存的正文。" || tags.Text != "伏笔，人物" || (await services.Projects.ListAsync()).Count != 1)
            throw new Exception("Canceling project creation changed the draft.");
        Prompt("新作品");
        await Until(() => create.IsEnabled && content.IsEnabled && projects.SelectedItem is Project { Name: "新作品" });
        var added = (Project)projects.SelectedItem;
        if (content.Text != "铜铃属于新作品，保留尚未保存的正文。" || tags.Text != "伏笔，人物") throw new Exception("Creating a project cleared draft fields.");
        if (services.Settings.LastProjectId != original.Id) throw new Exception("Unsaved project selection changed capture preference.");
        capture.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)capture.ActualWidth, (int)capture.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(capture);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.Combine(root, "capture-project.png"))) png.Save(stream);
        Click(capture, "保存灵感"); await Until(() => !capture.IsVisible);
        var saved = (await services.Ideas.ListAsync(new())).Single();
        if (saved.ProjectId != added.Id || saved.Tags.Count != 2 || services.Settings.LastProjectId != added.Id) throw new Exception("Capture did not save the selected new project and tags.");
        var reopened = new CaptureWindow(services); reopened.Show(); await Until(() => Find<TextBox>(reopened, "IdeaContent").IsEnabled);
        if ((long)Descendants(reopened).OfType<ComboBox>().Single().SelectedValue != added.Id) throw new Exception("Next capture did not remember project.");
        reopened.Close();
        await services.Ideas.SaveAsync(new("铜铃属于已有作品", original.Id, []));
        await services.Ideas.SaveAsync(new("铜铃没有所属作品", null, []));
        var controller = new DesktopController(services); controller.OpenChat();
        var chat = System.Windows.Application.Current.Windows.OfType<ChatWindow>().Single();
        await Until(() => Descendants(chat).OfType<ComboBox>().Any(c => c.Items.Count == 4));
        var filter = Descendants(chat).OfType<ComboBox>().Single(c => c.Items.OfType<Project>().Any());
        var question = Find<TextBox>(chat, "Question"); question.Text = "铜铃";
        foreach (var id in new[] { original.Id, added.Id, 0L, -1L })
        {
            filter.SelectedValue = id;
            foreach (var mode in new[] { "关键词", "混合搜索" })
            {
                Click(chat, mode); await Until(() => question.IsEnabled);
                var sources = Find<ListBox>(chat, "SearchSources").Items.OfType<Idea>().ToArray();
                if (sources.Length != (id == -1 ? 3 : 1) || id >= 0 && sources.Any(i => i.ProjectId != (id == 0 ? null : id))) throw new Exception("UI project filter leaked unrelated ideas.");
            }
        }
        chat.Close();
        await File.WriteAllTextAsync(Path.Combine(root, "project-result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = new[] { "new button beneath project selector", "cancel preserves selection and draft", "create selects project and preserves content/tags", "preference changes only after save", "saved idea belongs to new project", "next capture remembers project", "keyword project A/B/uncategorized/all", "AI-off hybrid project A/B/uncategorized/all" } }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
