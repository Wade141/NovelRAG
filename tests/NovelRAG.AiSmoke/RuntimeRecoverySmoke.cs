using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NovelRAG.AI.Ollama;
using NovelRAG.Application;
using NovelRAG.Desktop;

internal static class RuntimeRecoverySmoke
{
    public static async Task RunAsync(string root, string executable)
    {
        var profile = Path.Combine(root, "profile");
        if (File.Exists(Path.Combine(profile, "novelrag.db"))) throw new InvalidOperationException("Recovery smoke needs a fresh synthetic profile.");
        using var services = new AppServices(profile); await services.InitializeAsync();
        await services.UpdateSettingsAsync(s => s with { ManagedAi = true, AiEnabled = true, AiSetupChosen = true });
        await services.Ideas.SaveAsync(new("自动恢复验证：小鹿在渡口找到铜铃。", null, []));
        var controller = new DesktopController(services);
        using var monitorStop = new CancellationTokenSource(); Task monitor = Task.CompletedTask;
        using var external = new ManagedOllamaRuntime();
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            await services.Preparation.StartAsync(allowDownload: false);
            if (!services.Preparation.Status.SearchReady || !services.Preparation.Status.ChatReady) throw new Exception("Staged offline preparation failed.");
            controller.Supervisor.Activate(); monitor = controller.Supervisor.RunAsync(monitorStop.Token);
            var firstPid = KillOwned(services); Console.WriteLine("Terminated first test-owned engine: " + firstPid);
            await Until(() => services.AiBackend.OwnedProcessId is { } id && id != firstPid && services.Preparation.Status.SearchReady && !services.Preparation.IsRunning);
            var recoveredPid = services.AiBackend.OwnedProcessId!.Value;
            if (!services.Preparation.Status.ChatReady) throw new Exception("Recovery did not revalidate chat.");
            var found = await services.Search.SearchAsync(new("渡口"), "semantic"); if (found.Results.Count != 1) throw new Exception("Recovered semantic search failed.");
            var secondPid = KillOwned(services); Console.WriteLine("Terminated recovered engine: " + secondPid);
            await Until(() => services.Preparation.Status.Stage == AiStage.Failed && !services.Preparation.IsRunning);
            for (var i = 0; i < 3; i++) await controller.Supervisor.CheckOnceAsync();
            if (services.AiBackend.OwnedProcessId is not null || services.Preparation.Status.SearchReady || services.Preparation.Status.ChatReady) throw new Exception("Spent recovery budget restarted or retained readiness.");
            await services.Ideas.SaveAsync(new("恢复预算耗尽后仍可保存关键词灵感。", null, []));
            if ((await services.Search.SearchAsync(new("关键词"))).Results.Count != 1) throw new Exception("Keyword fallback failed after repeated crash.");

            await controller.StartAiPreparationAsync(false);
            await Until(() => !services.Preparation.IsRunning && services.Preparation.Status.SearchReady && services.AiBackend.OwnedProcessId is not null);
            var thirdPid = KillOwned(services);
            await Until(() => services.Preparation.IsRunning && controller.AiTransitioning);
            await controller.DisableAiAsync();
            await controller.Supervisor.CheckOnceAsync();
            if (services.Settings.AiEnabled || services.AiBackend.OwnedProcessId is not null || services.Preparation.Status.SearchReady) throw new Exception("Disable raced with recovery and left an engine running.");
            Console.WriteLine("Manual cycle rearmed recovery; disable canceled it and stopped owned processes.");

            var externalEndpoint = await external.StartAsync(executable, services.AiBackend.Root, "0.34.4", false, default);
            var externalPid = external.ProcessId;
            await services.UpdateSettingsAsync(s => s with { ManagedAi = false, AiEnabled = true, Endpoint = externalEndpoint });
            await controller.StartAiPreparationAsync(false);
            await Until(() => !services.Preparation.IsRunning && services.Preparation.Status.SearchReady);
            using var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var unusedPort = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
            await services.UpdateSettingsAsync(s => s with { Endpoint = "http://127.0.0.1:" + unusedPort });
            await controller.Supervisor.CheckOnceAsync(); await controller.Supervisor.CheckOnceAsync();
            await Until(() => services.Preparation.Status.Stage == AiStage.Failed);
            if (services.AiBackend.OwnedProcessId is not null || external.ProcessId != externalPid) throw new Exception("External mode took over a service.");
            await controller.Supervisor.SuspendAsync(); await monitorStop.CancelAsync(); await monitor;
            await services.Preparation.ShutdownAsync();
            using var response = await http.GetAsync(externalEndpoint + "/api/version"); response.EnsureSuccessStatusCode();
            using var outsider = new TcpClient(); await outsider.ConnectAsync(IPAddress.Loopback, 11450);
            await File.WriteAllTextAsync(Path.Combine(root, "recovery-result.json"), JsonSerializer.Serialize(new
            {
                Passed = true,
                FirstPid = firstPid,
                RecoveredPid = recoveredPid,
                SecondPid = secondPid,
                ThirdPid = thirdPid,
                ExternalPid = externalPid,
                Checks = new[] { "offline initial preparation", "owned exit triggers one automatic recovery", "recovery revalidates search and chat", "semantic search works after recovery", "second crash stops retries", "recording and keyword search survive", "manual start rearms budget", "disable cancels inflight recovery", "external failure does not take over service", "external service survives application shutdown", "occupied outsider port survives" }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            await controller.Supervisor.SuspendAsync(); await monitorStop.CancelAsync(); await monitor;
            await controller.Worker.DisposeAsync(); await services.Preparation.ShutdownAsync();
        }
    }
    private static int KillOwned(AppServices services)
    {
        var id = services.AiBackend.OwnedProcessId ?? throw new Exception("No owned process to test.");
        using var process = Process.GetProcessById(id);
        _ = process.SafeHandle;
        var expected = Path.GetFullPath(Path.Combine(services.AiBackend.Root, "runtime", "0.34.4", "ollama.exe"));
        if (!string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) throw new Exception("Test process identity does not match the staged engine.");
        process.Kill(); process.WaitForExit(5000); return id;
    }
    private static async Task Until(Func<bool> condition)
    {
        for (var n = 0; n < 600; n++) { if (condition()) return; await Task.Delay(200); }
        throw new TimeoutException("Runtime recovery smoke did not reach expected state.");
    }
}
