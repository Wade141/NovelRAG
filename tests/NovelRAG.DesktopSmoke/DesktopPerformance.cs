using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NovelRAG.Desktop;
using NovelRAG.Infrastructure;

namespace NovelRAG.DesktopSmoke;

internal static class DesktopPerformance
{
    public static async Task StartupProbeAsync(string profile)
    {
        var watch = Stopwatch.StartNew(); using var services = new AppServices(profile); await services.InitializeAsync();
        var controller = new DesktopController(services); controller.Start(); await controller.Pet.RendererTask;
        controller.OpenCapture(); var capture = System.Windows.Application.Current.Windows.OfType<CaptureWindow>().Single();
        await Until(() => Find<TextBox>(capture, "IdeaContent").IsEnabled); watch.Stop();
        await File.WriteAllTextAsync(Path.Combine(profile, "startup-probe.json"), JsonSerializer.Serialize(new { ReadyMs = watch.Elapsed.TotalMilliseconds }));
        capture.Close(); await controller.ExitAsync();
    }
    public static async Task RunAsync(string root, string datasets)
    {
        foreach (var count in new[] { 1000, 10000 })
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in new[] { Path.Combine(root, "capacity-" + count), "--performance-size", datasets, count.ToString() }) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5)); }
            finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
            if (child.ExitCode != 0) throw new Exception("Desktop performance child failed.");
        }
    }
    public static async Task RunSizeAsync(string profile, string datasets, int count)
    {
        Directory.CreateDirectory(profile);
        var path = Path.Combine(profile, "novelrag.db"); if (File.Exists(path)) throw new Exception("Fresh performance profile required.");
        var source = new SqliteStore(Path.Combine(datasets, "capacity-" + count, "novelrag.db")); await source.BackupAsync(path);
        var starts = new List<double>(); var ready = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add(profile); start.ArgumentList.Add("--startup-probe");
            var watch = Stopwatch.StartNew(); using var child = Process.Start(start)!;
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40)); }
            finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
            if (child.ExitCode != 0) throw new Exception("Startup probe failed.");
            starts.Add(watch.Elapsed.TotalMilliseconds);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(profile, "startup-probe.json"))); ready.Add(report.RootElement.GetProperty("ReadyMs").GetDouble());
        }
        using var services = new AppServices(profile); await services.InitializeAsync(); var controller = new DesktopController(services); controller.Start(); await controller.Pet.RendererTask;
        var entry = new List<double>(); var input = new List<double>(); var save = new List<double>(); var focus = 0;
        var sampler = new WorkingSetSampler();
        try
        {
            for (var i = 0; i < 30; i++)
            {
                var watch = Stopwatch.StartNew();
                controller.Pet.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                var capture = System.Windows.Application.Current.Windows.OfType<CaptureWindow>().Single();
                await Until(() => Find<TextBox>(capture, "IdeaContent").IsEnabled); entry.Add(watch.Elapsed.TotalMilliseconds);
                var content = Find<TextBox>(capture, "IdeaContent"); if (content.IsKeyboardFocused) focus++;
                watch.Restart(); content.Text = "固定短句：渡口的铜铃是辨认故人的信物。"; input.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart(); Click(capture, "保存灵感"); await Until(() => !System.Windows.Application.Current.Windows.OfType<CaptureWindow>().Any()); save.Add(watch.Elapsed.TotalMilliseconds);
            }
        }
        finally { await sampler.DisposeAsync(); }
        await File.WriteAllTextAsync(Path.Combine(profile, "desktop-performance.json"), JsonSerializer.Serialize(new
        {
            Count = count,
            Passed = true,
            ProcessColdReady = TimingSummary.From(ready),
            ProcessLifetime = TimingSummary.From(starts),
            Entry = TimingSummary.From(entry),
            ProgrammaticInput = TimingSummary.From(input),
            SaveFeedback = TimingSummary.From(save),
            FocusedSamples = focus,
            ClientSampledPeakBytes = sampler.PeakBytes,
            Limits = "Five fresh processes, OS cache not cleared. Ready timing starts at WPF Startup callback (excludes CLR/host startup); lifetime includes startup and clean exit. Ready/entry require enabled textbox; keyboard focus is observed separately, not guaranteed. 30 programmatic clicks/text assignments/saves; input timing is not human typing. AI disabled. Client memory only."
        }, new JsonSerializerOptions { WriteIndented = true }));
        await controller.ExitAsync();
    }
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    internal static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants(root).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    internal static void Click(DependencyObject root, string label) => Descendants(root).OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal static async Task Until(Func<bool> predicate, int seconds = 30)
    { var timer = Stopwatch.StartNew(); while (!predicate()) { if (timer.Elapsed.TotalSeconds > seconds) throw new TimeoutException("Smoke condition timed out."); await Task.Delay(5); } }
}
