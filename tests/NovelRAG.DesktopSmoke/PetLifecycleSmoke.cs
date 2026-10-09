using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelRAG.Desktop;

namespace NovelRAG.DesktopSmoke;

internal static class PetLifecycleSmoke
{
    public static async Task RunAsync(string root)
    {
        using var services = new AppServices(Path.Combine(root, "pet-lifecycle")); await services.InitializeAsync();
        var controller = new DesktopController(services); var renderers = new List<Probe>();
        var window = new PetWindow(services, controller, () => { var probe = new Probe(); renderers.Add(probe); return probe; });
        window.Show(); await window.RendererTask;
        if (!renderers.Single().Calls.SequenceEqual(new[] { "load", "visual", "scale:1", "idle" })) throw new Exception("Renderer lifecycle initialization order missing.");
        await services.UpdateSettingsAsync(s => s with { PetScale = 1.5 }); window.ApplySettings(); await window.RendererTask;
        if (window.Width != 165 || window.Height != 180 || renderers[0].Calls.Last() != "scale:1.5") throw new Exception("Renderer scale notification or window dimensions wrong.");
        window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        if (!renderers[0].Calls.Contains("click") || !System.Windows.Application.Current.Windows.OfType<CaptureWindow>().Any()) throw new Exception("Pet click failed to notify renderer and open capture.");
        System.Windows.Application.Current.Windows.OfType<CaptureWindow>().Single().Close();
        await window.PersistPositionAsync(); if ((await services.Configuration.LoadAsync()).PetX is null) throw new Exception("Position not saved.");
        window.Hide(); await window.RendererTask; if (renderers[0].Disposals != 1) throw new Exception("Hidden renderer was not released.");
        window.Show(); await window.RendererTask; if (renderers.Count != 2 || renderers[1].Calls[0] != "load") throw new Exception("Show did not create/load a new renderer.");
        window.Close(); await window.RendererTask; if (renderers[1].Disposals != 1) throw new Exception("Closed renderer was not released.");
        var failed = new Probe { FailLoad = true }; var fallback = new PetWindow(services, controller, () => failed);
        fallback.Show(); await fallback.RendererTask;
        if (failed.Disposals != 1 || fallback.Content is not Viewbox) throw new Exception("Failed renderer did not fall back to builtin owl.");
        fallback.Close(); await fallback.RendererTask;
        var blocked = new Probe { BlockLoad = true }; var pending = new PetWindow(services, controller, () => blocked);
        pending.Show(); await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3)); pending.Hide(); await pending.RendererTask.WaitAsync(TimeSpan.FromSeconds(3));
        if (blocked.Disposals != 1 || blocked.Calls.Contains("visual") || pending.Content is not null) throw new Exception("Canceled loading resurrected a hidden renderer.");
        pending.Close(); await pending.RendererTask;
        foreach (var transition in new[] { "hide", "hide-show", "close" })
        {
            var delayed = new Probe { DelayedLoad = true }; var replacement = new Probe(); var calls = 0;
            var racing = new PetWindow(services, controller, () => ++calls == 1 ? delayed : replacement);
            racing.Show(); await delayed.Started.Task;
            if (transition == "close") racing.Close();
            else { racing.Hide(); if (transition == "hide-show") racing.Show(); }
            delayed.Complete.TrySetResult(); await racing.RendererTask.WaitAsync(TimeSpan.FromSeconds(3));
            if (delayed.Disposals != 1 || delayed.Calls.Contains("visual")) throw new Exception("Late successful load attached after cancellation.");
            if (transition == "hide-show" && (calls != 2 || !replacement.Calls.Contains("visual") || racing.Content is null)) throw new Exception("Rapid show failed to load replacement.");
            if (transition != "hide-show" && racing.Content is not null) throw new Exception("Stopped window retained a visual.");
            if (transition != "close") { racing.Close(); await racing.RendererTask; }
            if (transition == "hide-show" && replacement.Disposals != 1) throw new Exception("Replacement renderer not disposed once.");
        }
        await controller.Worker.DisposeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "pet-lifecycle-result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = new[] { "load/visual/scale/idle order", "single geometric scale", "click opens capture", "position saved", "hide releases", "show reloads", "close releases", "load failure falls back", "hide cancels loading without resurrection", "late success after cancel never attaches", "rapid hide-show loads replacement", "close during load disposes once" } }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class Probe : IPetRenderer
    {
        public List<string> Calls { get; } = []; public int Disposals;
        public bool FailLoad, BlockLoad, DelayedLoad;
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task LoadAsync(string? assetPath, CancellationToken ct = default)
        {
            Calls.Add("load"); Started.SetResult();
            if (FailLoad) throw new IOException("Synthetic renderer load failure");
            if (BlockLoad) await Task.Delay(Timeout.Infinite, ct);
            if (DelayedLoad) await Complete.Task;
        }
        public FrameworkElement CreateVisual() { Calls.Add("visual"); return new Canvas(); }
        public void SetScale(double scale) => Calls.Add("scale:" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void PlayIdle() => Calls.Add("idle");
        public void PlayInteraction(string interaction) => Calls.Add(interaction);
        public void Dispose() { Disposals++; Calls.Add("dispose"); }
    }
}
