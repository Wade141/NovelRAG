using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace NovelRAG.AI.Ollama;

public sealed class ManagedOllamaRuntime : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private SafeFileHandle? job;
    private Process? process;
    public string? Endpoint { get; private set; }
    public int? ProcessId => process is { HasExited: false } ? process.Id : null;
    public async Task<string> StartAsync(string executable, string root, string version, bool cpuOnly, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("本地引擎需要 Windows。");
        await gate.WaitAsync(ct);
        try
        {
            StopCore();
            Directory.CreateDirectory(Path.Combine(root, "home")); Directory.CreateDirectory(Path.Combine(root, "models"));
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
            for (var port = 11450; port < 11460; port++)
            {
                ct.ThrowIfCancellationRequested();
                if (!Available(port)) continue;
                var env = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                    .Where(e => !((string)e.Key).StartsWith("OLLAMA_", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase);
                env["OLLAMA_HOST"] = $"127.0.0.1:{port}"; env["OLLAMA_MODELS"] = Path.Combine(root, "models"); env["OLLAMA_NO_CLOUD"] = "1";
                env["OLLAMA_NUM_PARALLEL"] = "1"; env["OLLAMA_MAX_LOADED_MODELS"] = "1"; env["OLLAMA_KEEP_ALIVE"] = "2m";
                env["OLLAMA_ORIGINS"] = "http://127.0.0.1"; env["OLLAMA_DEBUG"] = "0";
                env["HOME"] = env["USERPROFILE"] = Path.Combine(root, "home");
                if (cpuOnly) { env["CUDA_VISIBLE_DEVICES"] = "-1"; env["ROCR_VISIBLE_DEVICES"] = "-1"; env["GGML_VK_VISIBLE_DEVICES"] = "-1"; env["OLLAMA_VULKAN"] = "0"; }
                StartOwned(executable, env);
                var candidate = $"http://127.0.0.1:{port}";
                var deadline = Stopwatch.StartNew();
                while (deadline.Elapsed < TimeSpan.FromSeconds(15))
                {
                    ct.ThrowIfCancellationRequested();
                    if (process!.HasExited) break;
                    if (OwnsListener(port, process.Id))
                        try
                        {
                            using var response = await http.GetAsync(candidate + "/api/version", ct); response.EnsureSuccessStatusCode();
                            var content = await response.Content.ReadAsStringAsync(ct);
                            using var doc = JsonDocument.Parse(content);
                            if (doc.RootElement.GetProperty("version").GetString() == version && !process.HasExited && OwnsListener(port, process.Id))
                            { Endpoint = candidate; return candidate; }
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException or JsonException) { }
                    await Task.Delay(150, ct);
                }
                StopCore();
            }
            throw new InvalidOperationException("本地引擎未能启动。请检查安全软件是否拦截，或点击检查与修复。");
        }
        catch { StopCore(); throw; }
        finally { gate.Release(); }
    }
    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try { StopCore(); } finally { gate.Release(); }
    }
    private void StopCore()
    {
        // Closing the job terminates only this app's process tree, including model runners.
        job?.Dispose(); job = null;
        if (process is not null) { try { process.WaitForExit(5000); } catch (InvalidOperationException) { } process.Dispose(); process = null; }
        Endpoint = null;
    }
    public void Dispose() => StopCore();
    private static bool Available(int port)
    {
        try { using var listener = new TcpListener(IPAddress.Loopback, port); listener.Server.ExclusiveAddressUse = true; listener.Start(); return true; }
        catch (SocketException) { return false; }
    }
    private void StartOwned(string executable, Dictionary<string, string> environment)
    {
        job = CreateJobObject(IntPtr.Zero, null); if (job.IsInvalid) throw new Win32Exception();
        var limits = new JobLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<JobLimits>())) throw new Win32Exception();
        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        var block = string.Join('\0', environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value)) + "\0\0";
        var nativeEnvironment = Marshal.StringToHGlobalUni(block);
        ProcessInfo info = default;
        try
        {
            // Suspended until it belongs to our job: no child can escape during startup.
            if (!CreateProcess(executable, new StringBuilder("\"" + executable + "\" serve"), IntPtr.Zero, IntPtr.Zero, false,
                0x08000000 | 0x00000400 | 0x00000004, nativeEnvironment, Path.GetDirectoryName(executable)!, ref startup, out info)) throw new Win32Exception();
            if (!AssignProcessToJobObject(job, info.Process)) { TerminateProcess(info.Process, 1); throw new Win32Exception(); }
            process = Process.GetProcessById((int)info.ProcessId);
            if (ResumeThread(info.Thread) == uint.MaxValue) throw new Win32Exception();
        }
        finally
        {
            Marshal.FreeHGlobal(nativeEnvironment);
            if (info.Thread != IntPtr.Zero) CloseHandle(info.Thread);
            if (info.Process != IntPtr.Zero) CloseHandle(info.Process);
        }
    }
    private static bool OwnsListener(int port, int pid)
    {
        var size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(memory, ref size, false, 2, 3, 0) != 0) return false;
            var count = Marshal.ReadInt32(memory);
            for (var n = 0; n < count; n++)
            {
                var row = IntPtr.Add(memory, 4 + n * 24);
                var localPort = (ushort)IPAddress.NetworkToHostOrder((short)Marshal.ReadInt32(row, 8));
                if (localPort == port && Marshal.ReadInt32(row, 4) == 0x0100007f && Marshal.ReadInt32(row, 20) == pid) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo { public int cb; public string? Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags; public ushort ShowWindow, Reserved2; public IntPtr ReservedPointer, StdInput, StdOutput, StdError; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref JobLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
}
