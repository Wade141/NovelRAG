using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NovelRAG.Desktop;

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
                await services.InitializeAsync(); var controller = new DesktopController(services); controller.Start();
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
                Capture(library, Path.Combine(root, "library.png"));
                library.Close(); if (!controller.Pet.IsVisible) throw new Exception("Closing library affected pet.");
                await controller.Pet.PersistPositionAsync(); var saved = await services.Configuration.LoadAsync(); if (saved.PetX is null) throw new Exception("Pet position not saved.");
                await controller.TogglePetAsync(); if (controller.Pet.IsVisible) throw new Exception("Hide pet failed.");
                controller.OpenChat(); await Task.Delay(300); var chat = app.Windows.OfType<ChatWindow>().Single();
                Find<TextBox>(chat, "Question").Text = "铜铃"; Click(chat, "关键词");
                await Until(() => Find<TextBox>(chat, "Answer").Text.Contains("铜铃")); Capture(chat, Path.Combine(root, "search.png")); chat.Close();
                await controller.TogglePetAsync(); if (!controller.Pet.IsVisible) throw new Exception("Show pet failed.");
                var preparing = controller.PrepareRestoreAsync(); controller.OpenCapture(); await preparing;
                if (app.Windows.OfType<CaptureWindow>().Any()) throw new Exception("Capture opened during maintenance.");
                controller.FinishRestore();
                controller.OpenSettings(); await Task.Delay(500); var settings = app.Windows.OfType<SettingsWindow>().Single();
                if (Descendants(settings).OfType<TextBox>().Any(b => AutomationProperties.GetAutomationId(b) == "OllamaEndpoint" && b.IsVisible)) throw new Exception("Technical AI fields are visible by default.");
                var chooseLocation = Find<Button>(settings, "ChooseAiDirectory");
                if (!chooseLocation.IsVisible || !chooseLocation.IsEnabled) throw new Exception("Folder selection is not available on the default page.");
                if (!Descendants(settings).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == chosenRoot)) throw new Exception("The chosen directory is not displayed.");
                var prepare = Find<Button>(settings, "PrepareAi");
                await Until(() => prepare.IsEnabled);
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
                await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = new[] { "installed engine allows directory selection", "invalid destination preserves configuration", "directory selection persists after restart", "old components preserved", "folder selection visible without advanced settings", "preparation and maintenance prevent directory switch", "capture content/project/tags", "source opens editor", "library close keeps pet", "position persisted", "hide/show pet", "keyword search from hidden pet state", "default AI fields collapsed", "preparation button binding survives click", "closing settings keeps preparation running", "maintenance cancels preparation and blocks new start", "database backup" } }, new JsonSerializerOptions { WriteIndented = true }));
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
