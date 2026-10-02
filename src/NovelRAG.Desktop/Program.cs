using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace NovelRAG.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var dataIndex = Array.IndexOf(args, "--data-dir");
        var data = dataIndex >= 0 && dataIndex + 1 < args.Length ? Path.GetFullPath(args[dataIndex + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovelRAG");
        var key = "NovelRAG-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToUpperInvariant())))[..20];
        using var mutex = new Mutex(true, "Local\\" + key, out var first);
        if (!first)
        {
            try { using var pipe = new NamedPipeClientStream(".", key, PipeDirection.Out); pipe.Connect(3000); pipe.WriteByte(1); }
            catch (Exception) { MessageBox.Show("NovelRAG 已在运行，请从系统托盘打开。", "NovelRAG"); }
            return;
        }
        var app = new DesktopApp(data, key); app.Run();
        mutex.ReleaseMutex();
    }
}
internal sealed class DesktopApp(string data, string pipeName) : System.Windows.Application
{
    private AppServices? services;
    private DesktopController? controller;
    private readonly CancellationTokenSource stop = new();
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            services = new(data); await services.InitializeAsync(); controller = new(services); controller.Start();
            _ = ListenAsync();
        }
        catch (Exception ex) { services?.Log.Write("app.start_failed", ex); MessageBox.Show("启动失败，原始数据未被删除。\n" + ex.Message, "NovelRAG"); Shutdown(1); }
    }
    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                var buffer = new byte[1]; if (await pipe.ReadAsync(buffer, stop.Token) > 0) await Dispatcher.InvokeAsync(() => controller?.OpenCapture());
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { services?.Log.Write("ipc.failed", ex); await Task.Delay(1000); }
        }
    }
    protected override void OnExit(ExitEventArgs e) { stop.Cancel(); services?.Dispose(); base.OnExit(e); }
}
