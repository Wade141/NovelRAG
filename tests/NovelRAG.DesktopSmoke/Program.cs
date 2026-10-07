using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NovelRAG.Desktop;
using Microsoft.Data.Sqlite;

namespace NovelRAG.DesktopSmoke;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            using var services = new AppServices(root);
            try
            {
                await MigrationSmoke.RunAsync(root);
                await PetLifecycleSmoke.RunAsync(root);
                await services.InitializeAsync(); var controller = new DesktopController(services); controller.Start();
                await Task.Delay(50);
                await Task.WhenAll(controller.Worker.StopAsync(), controller.Worker.StopAsync()); controller.Worker.Start();
                var oldAiDirectory = services.AiBackend.Root;
                var installedEngine = Path.Combine(oldAiDirectory, "runtime", "0.34.4", "ollama.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(installedEngine)!);
                await File.WriteAllTextAsync(installedEngine, "installation marker in isolated test profile");
                using (var locationModel = new AiPreparationViewModel(services))
                    if (!locationModel.CanChooseDirectory) throw new Exception("Installed engine incorrectly disables component location selection.");
                var originalSettings = services.Settings;
                var invalidParent = Path.Combine(root, "not-a-folder"); await File.WriteAllTextAsync(invalidParent, "file");
                try { await controller.ChangeAiDirectoryAsync(invalidParent); throw new Exception("File was accepted as a component directory."); }
                catch (IOException) { }
                if (services.Settings != originalSettings) throw new Exception("Invalid folder changed existing configuration.");
                await services.UpdateSettingsAsync(s => s with { AiEnabled = true, ManagedAi = true, AiSetupChosen = true });
                var selectedParent = Path.Combine(root, "用户选择的位置");
                var chosenRoot = await controller.ChangeAiDirectoryAsync(selectedParent);
                if (!chosenRoot.StartsWith(selectedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Selected disk was ignored.");
                var locationSettings = await services.Configuration.LoadAsync();
                if (locationSettings.AiDirectory != chosenRoot || locationSettings.ModelsPath != Path.Combine(chosenRoot, "models") || locationSettings.AiEnabled) throw new Exception("New component directory was not saved safely.");
                if (services.Preparation.Status.SearchReady || services.Preparation.Status.ChatReady) throw new Exception("Old AI capabilities survived a directory change.");
                if (!File.Exists(installedEngine)) throw new Exception("Changing directory removed the old installation.");
                using (var reopened = new AppServices(root))
                { await reopened.InitializeAsync(); if (reopened.AiBackend.Root != chosenRoot) throw new Exception("Directory selection did not survive restart."); }
                var project = await services.Projects.SaveAsync("星河纪事 · 验证用");
                await services.Ideas.SaveAsync(new("北方战争之后，女主父亲留下了一张地下皇室交通网络图。", project.Id, ["世界观", "伏笔"]));
                controller.OpenCapture();
                var initializing = app.Windows.OfType<CaptureWindow>().Single();
                if (Find<TextBox>(initializing, "IdeaContent").IsEnabled) throw new Exception("Capture accepts text before initialization finishes.");
                await Task.Delay(600);
                var capture = app.Windows.OfType<CaptureWindow>().Single();
                Find<TextBox>(capture, "IdeaContent").Text = "女主童年在渡口遇见男主。他留给她一枚铜铃，多年后成为辨认身份的伏笔。";
                Find<TextBox>(capture, "IdeaTags").Text = "人物，伏笔，童年";
                Descendants(capture).OfType<ComboBox>().Single().SelectedValue = project.Id;
                Capture(capture, Path.Combine(root, "capture.png"));
                Click(capture, "保存灵感"); await Until(() => !app.Windows.OfType<CaptureWindow>().Any());
                var ideas = await services.Ideas.ListAsync(new()); if (ideas.Count != 2 || ideas[0].Tags.Count != 3 || ideas[0].ProjectId != project.Id) throw new Exception("Quick Capture did not persist metadata.");
                controller.OpenLibrary(ideas[0].Id); await Task.Delay(700);
                var library = app.Windows.OfType<LibraryWindow>().Single();
                if (!Find<TextBox>(library, "IdeaContent").Text.Contains("铜铃")) throw new Exception("Source navigation failed.");
                await VerifyLibraryDatesAsync(library, services, project.Id, root);
                Capture(library, Path.Combine(root, "library.png"));
                library.Close(); if (!controller.Pet.IsVisible) throw new Exception("Closing library affected pet.");
                await controller.Pet.PersistPositionAsync(); var saved = await services.Configuration.LoadAsync(); if (saved.PetX is null) throw new Exception("Pet position not saved.");
                await controller.TogglePetAsync(); if (controller.Pet.IsVisible) throw new Exception("Hide pet failed.");
                controller.OpenChat(); await Task.Delay(300); var chat = app.Windows.OfType<ChatWindow>().Single();
                Find<TextBox>(chat, "Question").Text = "铜铃"; Click(chat, "关键词");
                await Until(() => Find<TextBox>(chat, "Answer").Text.Contains("铜铃"));
                await VerifyChatDatesAsync(chat, app, services);
                Capture(chat, Path.Combine(root, "search.png")); chat.Close();
                await controller.TogglePetAsync(); if (!controller.Pet.IsVisible) throw new Exception("Show pet failed.");
                var preparing = controller.PrepareRestoreAsync(); controller.OpenCapture(); await preparing;
                if (app.Windows.OfType<CaptureWindow>().Any()) throw new Exception("Capture opened during maintenance.");
                controller.FinishRestore();
                controller.OpenSettings(); await Task.Delay(500); var settings = app.Windows.OfType<SettingsWindow>().Single();
                await VerifyRetrievalSettingsAsync(settings, services);
                await VerifyKeywordRepairAsync(settings, controller, services, app, root);
                if (Descendants(settings).OfType<TextBox>().Any(b => AutomationProperties.GetAutomationId(b) == "OllamaEndpoint" && b.IsVisible)) throw new Exception("Technical AI fields are visible by default.");
                var chooseLocation = Find<Button>(settings, "ChooseAiDirectory");
                if (!chooseLocation.IsVisible || !chooseLocation.IsEnabled) throw new Exception("Folder selection is not available on the default page.");
                if (!Descendants(settings).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == chosenRoot)) throw new Exception("The chosen directory is not displayed.");
                var prepare = Find<Button>(settings, "PrepareAi");
                await Until(() => prepare.IsEnabled);
                services.Preparation.RuntimeUnavailable(new NovelRAG.Application.AiRuntimeFailure(true, true));
                await Until(() => !prepare.IsEnabled && Equals(prepare.Content, "正在自动恢复"));
                if (Descendants(settings).OfType<Button>().Single(b => Equals(b.Content, "暂停")).IsEnabled) throw new Exception("Pause must be disabled before recovery preparation starts.");
                services.Preparation.RuntimeUnavailable(new NovelRAG.Application.AiRuntimeFailure(false, false));
                await Until(() => prepare.IsEnabled && Equals(prepare.Content, "检查与修复"));
                if (!Descendants(settings).OfType<TextBlock>().Any(t => t.Text.Contains("已有本机 AI 服务不可用"))) throw new Exception("External failure did not display the manual repair instruction.");
                Capture(settings, Path.Combine(root, "runtime-unavailable.png"));
                services.Preparation.Restore(new(NovelRAG.Application.AiStage.Disabled, "AI 已关闭"));
                await Until(() => prepare.IsEnabled && Equals(prepare.Content, "一键准备 AI"));
                Capture(settings, Path.Combine(root, "settings.png"));
                prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => services.Preparation.IsRunning);
                await Until(() => !chooseLocation.IsEnabled);
                try { await controller.ChangeAiDirectoryAsync(Path.Combine(root, "busy-location")); throw new Exception("Running preparation allowed a directory change."); }
                catch (InvalidOperationException) { }
                if (!System.Windows.Data.BindingOperations.IsDataBound(prepare, UIElement.IsEnabledProperty)) throw new Exception("Preparation button lost its enabled-state binding after click.");
                settings.Close();
                if (!services.Preparation.IsRunning) throw new Exception("Closing settings canceled preparation.");
                if (!await controller.PrepareRestoreAsync()) throw new Exception("Could not enter maintenance.");
                await AssertBlockedAsync(services);
                try { await controller.ChangeAiDirectoryAsync(Path.Combine(root, "maintenance-location")); throw new Exception("Maintenance allowed a directory change."); }
                catch (InvalidOperationException) { }
                controller.FinishRestore();
                await services.Preparation.PauseAsync();
                await services.Data.BackupAsync(Path.Combine(root, "backup.db"));
                await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = new[] { "installed engine allows directory selection", "invalid destination preserves configuration", "directory selection persists after restart", "old components preserved", "folder selection visible without advanced settings", "preparation and maintenance prevent directory switch", "capture content/project/tags", "source opens editor", "library close keeps pet", "position persisted", "hide/show pet", "keyword search from hidden pet state", "default AI fields collapsed", "preparation button binding survives click", "closing settings keeps preparation running", "maintenance cancels preparation and blocks new start", "database backup", "date calendar selection", "inclusive library dates", "invalid dates preserve input and results", "clear dates restores results", "changed filter resets page", "keyword and fallback hybrid sources obey dates", "filtered source opens original note", "archive with invalid dates reconciles list and editor", "invalid retrieval settings preserve configuration", "retrieval settings survive restart", "defaults require save", "Chat SearchTopK applies", "Library stays at 50 with SearchTopK 1", "concurrent index stop joins safely", "automatic recovery disables preparation entry", "external outage offers manual repair", "keyword repair works without AI", "keyword maintenance blocks capture and restore", "canceled keyword repair does not commit", "keyword repair preserves source metadata" } }, new JsonSerializerOptions { WriteIndented = true }));
                await controller.ExitAsync();
            }
            catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(root, "failure.txt"), ex.ToString()); app.Shutdown(1); }
        };
        app.Run();
    }
    private static async Task AssertBlockedAsync(AppServices services)
    {
        try { await services.Preparation.StartAsync(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Maintenance permitted a new preparation task.");
    }
    private static async Task VerifyKeywordRepairAsync(SettingsWindow settings, DesktopController controller, AppServices services, System.Windows.Application app, string root)
    {
        if (services.Settings.AiEnabled) throw new Exception("Keyword repair regression requires AI disabled.");
        var data = Descendants(settings).OfType<Expander>().Single(e => Equals(e.Header, "数据与备份")); data.IsExpanded = true;
        await Task.Delay(80);
        var repair = Find<Button>(settings, "RepairKeywordIndex"); if (!repair.IsEnabled || !repair.IsVisible) throw new Exception("Keyword repair unavailable without AI.");
        var store = new NovelRAG.Infrastructure.SqliteStore(services.Data.DatabasePath); var before = await store.SnapshotAsync();
        using (var connection = new SqliteConnection($"Data Source={services.Data.DatabasePath};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO IdeasFts(IdeasFts) VALUES('delete-all'); INSERT INTO IdeasTrigram(IdeasTrigram) VALUES('delete-all'); INSERT INTO IdeasTrigram(rowid,Content) SELECT Id,'phantom' FROM Ideas"; command.ExecuteNonQuery();
            using (var tx = connection.BeginTransaction())
            {
                repair.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => Find<TextBlock>(settings, "SettingsStatus").Text.Contains("正在修复"));
                controller.OpenCapture(); if (app.Windows.OfType<CaptureWindow>().Any()) throw new Exception("Capture opened during keyword maintenance.");
                if (await controller.PrepareRestoreAsync()) throw new Exception("Nested restore entered keyword maintenance.");
                Click(settings, "取消当前索引 / 操作");
                tx.Commit();
            }
        }
        await Until(() => Find<TextBlock>(settings, "SettingsStatus").Text.Contains("已取消"));
        if ((await services.Search.SearchAsync(new("phantom"))).Results.Count == 0) throw new Exception("Canceled repair unexpectedly committed.");
        repair.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Find<TextBlock>(settings, "SettingsStatus").Text.Contains("已修复并校验"));
        if ((await services.Search.SearchAsync(new("phantom"))).Results.Count != 0) throw new Exception("Keyword repair retained stale index rows.");
        var after = await store.SnapshotAsync();
        if (JsonSerializer.Serialize(before with { ExportedAt = after.ExportedAt }) != JsonSerializer.Serialize(after)) throw new Exception("Keyword repair changed source records.");
        Capture(settings, Path.Combine(root, "keyword-repaired.png")); data.IsExpanded = false;
    }
    private static async Task VerifyLibraryDatesAsync(Window library, AppServices services, long projectId, string root)
    {
        var originalOptions = services.Settings.Retrieval;
        await services.UpdateSettingsAsync(s => s with { Retrieval = s.Retrieval with { SearchTopK = 1, RagTopK = 1 } });
        var start = Find<TextBox>(library, "DateRangeStart"); var end = Find<TextBox>(library, "DateRangeEnd");
        var results = Find<ListBox>(library, "LibraryResults"); var search = Find<TextBox>(library, "LibrarySearch");
        async Task Add(string content, int day)
        {
            var idea = await services.Ideas.SaveAsync(new(content, projectId, []));
            var local = new DateTime(2026, 10, day, 12, 0, 0, DateTimeKind.Unspecified);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "novelrag.db"), Pooling = false }.ToString());
            await connection.OpenAsync(); await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Ideas SET CreatedAt = $date WHERE Id = $id";
            command.Parameters.AddWithValue("$id", idea.Id);
            command.Parameters.AddWithValue("$date", new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
        await Add("日期铜铃范围内", 7); await Add("日期铜铃范围外", 8);
        Find<Button>(library, "DateRangeStartCalendar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var calendar = PresentationSource.CurrentSources.Cast<PresentationSource>().SelectMany(s => Descendants(s.RootVisual)).OfType<Calendar>().Single(c => c.IsVisible);
        calendar.SelectedDate = new DateTime(2026, 10, 7);
        if (start.Text != "2026-10-07") throw new Exception("Calendar selection did not set the start date.");
        search.Text = "日期铜铃"; end.Text = "2026-10-07";
        Click(library, "搜索"); await Until(() => search.IsEnabled && results.Items.Count == 1);
        if (((NovelRAG.Domain.Idea)results.Items[0]).Content != "日期铜铃范围内") throw new Exception("Library date range returned an outside record.");
        end.Text = "2026-10-06"; Click(library, "搜索"); await Until(() => search.IsEnabled);
        if (!Find<TextBlock>(library, "DateRangeError").IsVisible || end.Text != "2026-10-06" || results.Items.Count != 1) throw new Exception("Reversed date did not preserve input and results.");
        end.Text = "2026-99-99"; Click(library, "搜索"); await Until(() => search.IsEnabled);
        if (!Find<TextBlock>(library, "DateRangeError").IsVisible || end.Text != "2026-99-99") throw new Exception("Invalid date input was discarded.");
        var selected = (NovelRAG.Domain.Idea)results.Items[0];
        results.SelectedIndex = 0; await Until(() => Find<TextBox>(library, "IdeaContent").IsEnabled);
        Click(library, "归档 / 取消归档"); await Until(() => search.IsEnabled);
        if (results.Items.Count != 0 || Find<TextBox>(library, "IdeaContent").IsEnabled || end.Text != "2026-99-99") throw new Exception("Archive with an invalid date left stale editable results.");
        if (!(await services.Ideas.GetAsync(selected.Id))!.Archived) throw new Exception("Archive did not persist.");
        await services.Ideas.ArchiveAsync(selected.Id, false);
        Click(library, "清空日期"); Click(library, "搜索"); await Until(() => search.IsEnabled && results.Items.Count == 2);
        if (start.Text.Length != 0 || end.Text.Length != 0) throw new Exception("Clear dates failed.");
        for (var i = 0; i < 51; i++) await Add("日期分页测试 " + i, 7);
        search.Text = "日期分页测试"; Click(library, "搜索"); await Until(() => search.IsEnabled && results.Items.Count == 50);
        Click(library, "下一页"); await Until(() => search.IsEnabled && results.Items.Count == 1);
        start.Text = end.Text = "2026-10-07"; Click(library, "下一页"); await Until(() => search.IsEnabled && results.Items.Count == 50);
        if (!Find<TextBlock>(library, "LibraryStatus").Text.StartsWith("第 1 页")) throw new Exception("Changed date filter did not reset paging.");
        search.Text = "日期铜铃"; Click(library, "搜索"); await Until(() => search.IsEnabled && results.Items.Count == 1);
        await services.UpdateSettingsAsync(s => s with { Retrieval = originalOptions });
    }
    private static async Task VerifyRetrievalSettingsAsync(Window settings, AppServices services)
    {
        var expander = Find<Expander>(settings, "RetrievalOptionsExpander"); expander.IsExpanded = true; settings.UpdateLayout();
        var weight = Find<TextBox>(settings, "RetrievalVectorWeight"); var status = Find<TextBlock>(settings, "RetrievalStatus");
        var original = await File.ReadAllTextAsync(Path.Combine(services.DataDirectory, "settings.json"));
        weight.Text = "-1"; Click(settings, "保存检索参数"); await Until(() => status.Text.Contains("权重"));
        if (original != await File.ReadAllTextAsync(Path.Combine(services.DataDirectory, "settings.json"))) throw new Exception("Invalid retrieval settings overwrote configuration.");
        weight.Text = "1.5"; Find<TextBox>(settings, "RetrievalSearchTopK").Text = "12"; Find<TextBox>(settings, "RetrievalRagTopK").Text = "4";
        Click(settings, "保存检索参数"); await Until(() => status.Text.Contains("已保存"));
        var saved = await services.Configuration.LoadAsync();
        if (saved.Retrieval.VectorWeight != 1.5 || saved.Retrieval.SearchTopK != 12 || saved.Retrieval.RagTopK != 4) throw new Exception("Retrieval settings did not persist.");
        using (var reopened = new AppServices(services.DataDirectory))
        { await reopened.InitializeAsync(); if (reopened.Settings.Retrieval != saved.Retrieval) throw new Exception("Retrieval settings changed after restart."); }
        expander.BringIntoView(); await Task.Delay(100); Capture(settings, Path.Combine(services.DataDirectory, "retrieval-settings.png"));
        Click(settings, "恢复默认检索参数");
        if (services.Settings.Retrieval.VectorWeight != 1.5 || weight.Text != "0.5") throw new Exception("Restoring defaults should edit fields before save.");
        Click(settings, "保存检索参数"); await Until(() => services.Settings.Retrieval == new NovelRAG.Application.RetrievalOptions());
        expander.IsExpanded = false;
        Descendants(settings).OfType<ScrollViewer>().First().ScrollToTop();
    }
    private static async Task VerifyChatDatesAsync(Window chat, System.Windows.Application app, AppServices services)
    {
        var question = Find<TextBox>(chat, "Question"); var answer = Find<TextBox>(chat, "Answer"); var sources = Find<ListBox>(chat, "SearchSources");
        question.Text = "日期铜铃"; Find<TextBox>(chat, "DateRangeStart").Text = Find<TextBox>(chat, "DateRangeEnd").Text = "2026-10-07";
        foreach (var mode in new[] { "关键词", "混合搜索" })
        {
            Click(chat, mode); await Until(() => question.IsEnabled && sources.Items.Count == 1);
            if (!answer.Text.Contains("日期铜铃范围内") || answer.Text.Contains("范围外")) throw new Exception("Search date range leaked an outside source.");
        }
        var previous = answer.Text;
        Find<TextBox>(chat, "DateRangeEnd").Text = "bad date"; Click(chat, "关键词");
        if (!Find<TextBlock>(chat, "DateRangeError").IsVisible || answer.Text != previous) throw new Exception("Invalid chat date replaced previous answer.");
        sources.SelectedIndex = 0; Click(chat, "打开选中来源");
        await Until(() => app.Windows.OfType<LibraryWindow>().Any(w => Find<TextBox>(w, "IdeaContent").Text == "日期铜铃范围内"));
        app.Windows.OfType<LibraryWindow>().Single().Close();
        Click(chat, "清空日期"); Click(chat, "关键词"); await Until(() => question.IsEnabled && sources.Items.Count == 2);
        var original = services.Settings.Retrieval;
        await services.UpdateSettingsAsync(s => s with { Retrieval = s.Retrieval with { SearchTopK = 1, RagTopK = 1 } });
        Click(chat, "关键词"); await Until(() => question.IsEnabled && sources.Items.Count == 1);
        await services.UpdateSettingsAsync(s => s with { Retrieval = original });
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static T Find<T>(Window window, string id) where T : DependencyObject => Descendants(window).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static void Click(Window window, string label) => Descendants(window).OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++) { if (condition()) return; await Task.Delay(100); }
        throw new TimeoutException("Window action did not finish.");
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
