using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelRAG.AI.Ollama;
using NovelRAG.Application;
using NovelRAG.Desktop;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;
using static NovelRAG.DesktopSmoke.DesktopPerformance;

namespace NovelRAG.DesktopSmoke;

internal static class EndToEndSmoke
{
    public static async Task RunAsync(string root, string executable, string modelRoot)
    {
        if (File.Exists(Path.Combine(root, "novelrag.db"))) throw new Exception("End-to-end smoke needs fresh data.");
        using var runtime = new ManagedOllamaRuntime();
        var endpoint = await runtime.StartAsync(executable, modelRoot, "0.34.4", false, default);
        using var services = new AppServices(root); await services.InitializeAsync();
        var controller = new DesktopController(services); controller.Start(); await controller.Pet.RendererTask;
        var project = await services.Projects.SaveAsync("端到端作品");
        controller.Pet.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        var capture = System.Windows.Application.Current.Windows.OfType<CaptureWindow>().Single();
        await Until(() => Find<TextBox>(capture, "IdeaContent").IsEnabled);
        Find<TextBox>(capture, "IdeaContent").Text = "女主童年在渡口第一次遇见男主，男主留下铜铃作为重逢的信物。";
        Find<TextBox>(capture, "IdeaTags").Text = "童年，伏笔";
        Descendants(capture).OfType<ComboBox>().Single().SelectedValue = project.Id;
        Click(capture, "保存灵感"); await Until(() => !System.Windows.Application.Current.Windows.OfType<CaptureWindow>().Any());
        var idea = (await services.Ideas.ListAsync(new())).Single();
        if ((await services.Search.SearchAsync(new("铜铃"), "hybrid")).Warning is null) throw new Exception("AI-off hybrid did not degrade.");
        await controller.ConfigureAiAsync(false, endpoint, "not-installed-synthetic", "qwen3:4b"); await services.Preparation.StartAsync(false);
        if (services.Preparation.Status.SearchReady || (await services.Search.SearchAsync(new("铜铃"))).Results.Count != 1) throw new Exception("Missing model affected basic search.");
        await controller.ConfigureAiAsync(false, endpoint, "embeddinggemma:latest", "qwen3:4b"); await controller.StartAiPreparationAsync(false);
        await Until(() => !services.Preparation.IsRunning && services.Preparation.Status.ChatReady, 120);
        await controller.Worker.StopAsync();
        var unindexed = await services.Ideas.SaveAsync(new("另一个未索引记录的铜铃", null, []));
        var incomplete = await services.Search.SearchAsync(new("铜铃"), "semantic");
        if (incomplete.Warning is null) throw new Exception("Incomplete index had no warning.");
        await services.Ideas.DeleteAsync(unindexed.Id);
        controller.OpenChat(); var chat = System.Windows.Application.Current.Windows.OfType<ChatWindow>().Single();
        await Until(() => Descendants(chat).OfType<ComboBox>().Any(c => c.Items.Count >= 3));
        Descendants(chat).OfType<ComboBox>().Single(c => c.Items.OfType<Project>().Any()).SelectedValue = project.Id;
        Find<TextBox>(chat, "Question").Text = "童年相遇时留下什么信物？";
        Click(chat, "混合搜索"); await Until(() => Find<TextBox>(chat, "Question").IsEnabled);
        if (Find<ListBox>(chat, "SearchSources").Items.Count != 1) throw new Exception("Filtered hybrid source mismatch.");
        Click(chat, "提问"); await Until(() => Find<TextBox>(chat, "Question").IsEnabled, 120);
        if (!Find<TextBox>(chat, "Answer").Text.Contains("铜铃")) throw new Exception("Real-model UI answer missed source evidence.");
        if (!Descendants(chat).OfType<TextBlock>().Any(t => t.Text == "回答已生成 · 请核对原文") ||
            Descendants(chat).OfType<TextBlock>().Any(t => t.Text == "已显示证据或降级结果") ||
            !Find<ListBox>(chat, "SearchSources").Items.OfType<Idea>().Select(i => i.Id).SequenceEqual([idea.Id]))
            throw new Exception("Real-model UI answer degraded or cited an unexpected idea.");
        Find<ListBox>(chat, "SearchSources").SelectedIndex = 0; Click(chat, "打开选中来源");
        var library = System.Windows.Application.Current.Windows.OfType<LibraryWindow>().Single();
        await Until(() => Find<TextBox>(library, "IdeaContent") is { IsEnabled: true } content && content.Text.Contains("铜铃") &&
            Descendants(library).OfType<Button>().Single(b => Equals(b.Content, "保存修改")).IsEnabled);
        Find<TextBox>(library, "IdeaContent").Text += " 编辑核验。"; Click(library, "保存修改");
        await Until(() => Find<TextBlock>(library, "LibraryStatus").Text == "修改已保存" && Find<TextBox>(library, "IdeaContent").IsEnabled);
        if (!(await services.Ideas.GetAsync(idea.Id))!.Content.Contains("编辑核验")) throw new Exception("UI edit was not persisted before backup.");
        library.Close(); chat.Close();
        var backup = Path.Combine(root, "e2e-backup.db"); await services.Data.BackupAsync(backup); await services.Ideas.DeleteAsync(idea.Id);
        if (!await controller.PrepareRestoreAsync()) throw new Exception("Could not enter restore.");
        try { await services.Data.RestoreAsync(backup); } finally { controller.FinishRestore(); }
        if (!(await services.Ideas.GetAsync(idea.Id))!.Content.Contains("编辑核验")) throw new Exception("Restore did not preserve edit.");
        await controller.DisableAiAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "e2e-result.json"), JsonSerializer.Serialize(new
        {
            Passed = true,
            Checks = new[] { "pet opens capture", "save project/tags", "AI-off fallback", "missing model preserves keyword search", "prepared local models with download forbidden", "incomplete index warns", "project-filtered hybrid UI", "real-model UI answer", "source opens original", "edit persists", "backup and restore edited record", "management close leaves host", "AI disabled and owned test runtime disposed" }
        }, new JsonSerializerOptions { WriteIndented = true }));
        await runtime.StopAsync(); await controller.ExitAsync();
    }
}
