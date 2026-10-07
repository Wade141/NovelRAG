using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NovelRAG.AI.Ollama;
using NovelRAG.Desktop;
using NovelRAG.Application;
using NovelRAG.Infrastructure;

var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
var exe = Path.GetFullPath(args[1]);
if (args.Contains("--crash-child"))
{
    using var owned = new ManagedOllamaRuntime(); await owned.StartAsync(exe, root, "0.34.4", false, default);
    await File.WriteAllTextAsync(Path.Combine(root, "crash-owned-pid.txt"), owned.ProcessId.ToString());
    await Task.Delay(Timeout.Infinite); return;
}
using var outsider = new TcpListener(IPAddress.Loopback, 11450); outsider.Start();
using var runtime = new ManagedOllamaRuntime();
var endpoint = await runtime.StartAsync(exe, root, "0.34.4", false, default);
if (endpoint.EndsWith(":11450")) throw new Exception("Runtime took occupied external port.");
var pid = runtime.ProcessId!.Value;
await runtime.StopAsync();
try { using var remaining = Process.GetProcessById(pid); if (!remaining.HasExited) throw new Exception("Owned process survived shutdown."); } catch (ArgumentException) { }
using var connected = new TcpClient(); await connected.ConnectAsync(IPAddress.Loopback, 11450);
await File.WriteAllTextAsync(Path.Combine(root, "runtime-result.json"), JsonSerializer.Serialize(new { Passed = true, Endpoint = endpoint, StoppedPid = pid }));
Console.WriteLine("Runtime ownership, occupied port and cleanup passed.");

if (args.Contains("--recovery-check")) await RuntimeRecoverySmoke.RunAsync(root, exe);

if (args.Contains("--change-location"))
{
    using var services = new AppServices(Path.Combine(root, "profile")); await services.InitializeAsync();
    await services.UpdateSettingsAsync(s => s with { AiDirectory = Path.Combine(root, "profile", "ai"), ModelsPath = Path.Combine(root, "profile", "ai", "models"), ManagedAi = true, AiSetupChosen = true, AiEnabled = true });
    await services.AiBackend.PrepareEngineAsync(false, new InlineProgress<AiTransferProgress>(_ => { }), default);
    var oldRoot = services.AiBackend.Root;
    var oldEndpoint = services.Settings.Endpoint;
    using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
    using (var response = await http.GetAsync(oldEndpoint + "/api/version")) response.EnsureSuccessStatusCode();
    using var ownedProcess = Process.GetProcessesByName("ollama").Single(p => string.Equals(p.MainModule?.FileName, Path.Combine(oldRoot, "runtime", "0.34.4", "ollama.exe"), StringComparison.OrdinalIgnoreCase));
    var controller = new DesktopController(services);
    try
    {
        var newRoot = await controller.ChangeAiDirectoryAsync(Path.Combine(root, "changed-location-" + Guid.NewGuid().ToString("N")));
        if (services.Settings.AiEnabled || services.AiBackend.Root != newRoot) throw new Exception("Directory switch did not deactivate old configuration.");
        await ownedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (!File.Exists(Path.Combine(oldRoot, "runtime", "0.34.4", "ollama.exe"))) throw new Exception("Old engine was deleted.");
        await File.WriteAllTextAsync(Path.Combine(root, "location-result.json"), JsonSerializer.Serialize(new { Passed = true, NewRoot = newRoot, Checks = new[] { "live owned runtime stopped before switching", "old engine preserved", "new configuration persisted" } }));
    }
    finally { await controller.Worker.DisposeAsync(); await services.Preparation.ShutdownAsync(); }
}

if (args.Contains("--crash-check"))
{
    var childRoot = Path.Combine(root, "crash-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(childRoot);
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
    start.ArgumentList.Add(childRoot); start.ArgumentList.Add(exe); start.ArgumentList.Add("--crash-child");
    using var child = Process.Start(start)!;
    var pidPath = Path.Combine(childRoot, "crash-owned-pid.txt");
    try
    {
        for (var n = 0; !File.Exists(pidPath) && n < 200; n++) await Task.Delay(100);
        var ownedPid = int.Parse(await File.ReadAllTextAsync(pidPath));
        using var ownedProcess = Process.GetProcessById(ownedPid);
        child.Kill(); await child.WaitForExitAsync();
        await ownedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await File.WriteAllTextAsync(Path.Combine(root, "crash-result.json"), JsonSerializer.Serialize(new { Passed = true, OwnedPid = ownedPid }));
        Console.WriteLine("Job closed owned runtime after abrupt host termination.");
    }
    finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
}

if (args.Contains("--pull-check"))
{
    var installedRoot = Path.Combine(root, "profile", "ai");
    var server = await runtime.StartAsync(Path.Combine(installedRoot, "runtime", "0.34.4", "ollama.exe"), installedRoot, "0.34.4", false, default);
    var model = ComponentCatalog.Load().Models[0];
    var small = model.Blobs.First(b => b.Size > 1000 && b.Size < 10000);
    File.Delete(ComponentFiles.SafeChild(Path.Combine(installedRoot, "models"), small.Path));
    using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
    using var cancel = new CancellationTokenSource();
    try
    {
        await new OllamaModelDownloader(http).PullAsync(server, model.Name, new InlineProgress<AiTransferProgress>(p => { if (p.Total > 0) cancel.Cancel(); }), cancel.Token);
        throw new Exception("Pull did not observe requested cancellation.");
    }
    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
    await new OllamaModelDownloader(http).PullAsync(server, model.Name, new InlineProgress<AiTransferProgress>(p => Console.WriteLine(p.Message)), default);
    if (!await AiComponentInstaller.ModelValidAsync(model, Path.Combine(installedRoot, "models"), default)) throw new Exception("Resumed pull did not restore pinned model.");
    await runtime.StopAsync();
    await File.WriteAllTextAsync(Path.Combine(root, "pull-result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = new[] { "stream cancellation", "resume existing layers", "restore missing license blob", "pinned model hash" } }));
}

if (args.Contains("--large-resume"))
{
    var downloadRoot = Path.Combine(root, "large-resume");
    var server = await runtime.StartAsync(exe, downloadRoot, "0.34.4", false, default);
    using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
    var model = ComponentCatalog.Load().Models[0];
    using var cancel = new CancellationTokenSource(); long interruptedAt = 0;
    try
    {
        await new OllamaModelDownloader(http).PullAsync(server, model.Name, new InlineProgress<AiTransferProgress>(p =>
        { if (p.Total > 100_000_000 && p.Completed > 1_000_000 && p.Completed < p.Total) { interruptedAt = p.Completed; cancel.Cancel(); } }), cancel.Token);
        throw new Exception("Could not interrupt a partially downloaded large layer.");
    }
    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
    await runtime.StopAsync();
    var partials = Directory.GetFiles(Path.Combine(downloadRoot, "models", "blobs"), "*-partial*");
    if (partials.Length == 0) throw new Exception("Interrupted transfer did not retain partial layers.");
    server = await runtime.StartAsync(exe, downloadRoot, "0.34.4", false, default);
    long resumedAt = -1;
    await new OllamaModelDownloader(http).PullAsync(server, model.Name, new InlineProgress<AiTransferProgress>(p =>
    { if (p.Total > 100_000_000 && p.Completed > 0 && resumedAt < 0) { resumedAt = p.Completed; Console.WriteLine("Resumed layer at " + resumedAt); } }), default);
    if (!await AiComponentInstaller.ModelValidAsync(model, Path.Combine(downloadRoot, "models"), default)) throw new Exception("Cross-process layer resume failed validation.");
    await runtime.StopAsync();
    await File.WriteAllTextAsync(Path.Combine(root, "large-resume-result.json"), JsonSerializer.Serialize(new { Passed = true, InterruptedAt = interruptedAt, ResumedAt = resumedAt, RetainedPartialFiles = partials.Length }));
    Console.WriteLine("Cross-process partial large-layer resume passed.");
}

if (args.Contains("--full"))
{
    using var services = new AppServices(Path.Combine(root, "profile"));
    await services.InitializeAsync();
    await services.UpdateSettingsAsync(s => s with { ManagedAi = true, AiSetupChosen = true });
    await services.Ideas.SaveAsync(new("女主小时候在渡口遇见男主，信物是铜铃。", null, []));
    services.Preparation.Changed += () => Console.WriteLine(services.Preparation.Status.Message + " " + services.Preparation.Status.Transfer?.Message);
    await services.Preparation.StartAsync();
    if (services.Preparation.Status.Stage != AiStage.Ready) throw new Exception(JsonSerializer.Serialize(services.Preparation.Status));
    var answer = await services.Chat.AskAsync(new("女主和男主小时候在哪里相遇，信物是什么？"));
    if (answer.IsFallback || answer.Sources.Count == 0 || !answer.Text.Contains("铜铃")) throw new Exception("Prepared AI failed cited Chinese answer: " + answer.Text);
    await services.Preparation.ShutdownAsync();
    using var restart = new AppServices(Path.Combine(root, "profile")); await restart.InitializeAsync();
    await restart.Preparation.StartAsync(allowDownload: false);
    if (restart.Preparation.Status.Stage != AiStage.Ready) throw new Exception("Offline restart failed: " + restart.Preparation.Status.Detail);
    await restart.Preparation.ShutdownAsync();
    await File.WriteAllTextAsync(Path.Combine(root, "preparation-result.json"), JsonSerializer.Serialize(new { Passed = true, Answer = answer.Text, Checks = new[] { "verified local import", "managed runtime", "automatic indexing", "cited Chinese answer", "offline preparation restart", "owned shutdown" } }));
    Console.WriteLine("Full preparation, indexing, answer and offline restart passed.");
}
