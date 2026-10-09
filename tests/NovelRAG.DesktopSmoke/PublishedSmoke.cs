using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

namespace NovelRAG.DesktopSmoke;

internal static class PublishedSmoke
{
    public static async Task RunAsync(string root, string executable)
    {
        foreach (var legacy in new[] { false, true })
        {
            var profile = Path.Combine(root, legacy ? "v1-profile" : "new-profile"); Directory.CreateDirectory(profile);
            if (legacy)
            {
                using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(profile, "novelrag.db")};Pooling=False"); c.Open();
                using var cmd = c.CreateCommand(); cmd.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1.sql")) + "INSERT INTO Ideas(Content,CreatedAt,UpdatedAt) VALUES('发布升级合成灵感','2020-01-01T00:00:00.0000000+00:00','2020-01-01T00:00:00.0000000+00:00');"; cmd.ExecuteNonQuery();
            }
            ProcessStartInfo Start()
            {
                var start = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!, WindowStyle = ProcessWindowStyle.Hidden };
                start.Environment["DOTNET_ROOT"] = Path.Combine(root, "no-dotnet"); start.Environment["DOTNET_ROOT_X64"] = Path.Combine(root, "no-dotnet"); start.Environment["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
                start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(profile); return start;
            }
            using var child = Process.Start(Start())!;
            var graceful = false;
            try
            {
                AutomationElement? pet = null;
                await DesktopPerformance.Until(() => { pet = Windows(child.Id).FirstOrDefault(w => w.Current.Name == "NovelRAG 灵感桌宠"); return pet is not null; });
                var clr = child.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.ModuleName, "coreclr.dll", StringComparison.OrdinalIgnoreCase));
                if (!clr.FileName.StartsWith(Path.GetDirectoryName(Path.GetFullPath(executable))! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Published process did not load its bundled CLR.");
                using (var second = Process.Start(Start())!)
                {
                    try
                    {
                        await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                        if (second.ExitCode != 0) throw new Exception("Second instance did not forward successfully.");
                    }
                    finally { if (!second.HasExited) { second.Kill(); await second.WaitForExitAsync(); } }
                }
                AutomationElement? capture = null;
                await DesktopPerformance.Until(() => { capture = Windows(child.Id).FirstOrDefault(w => w.Current.Name.Contains("记录灵感")); return capture is not null; });
                ((WindowPattern)capture!.GetCurrentPattern(WindowPattern.Pattern)).Close(); await Task.Delay(100);
                if (child.HasExited) throw new Exception("Closing capture stopped tray host.");
                // WPF hit-tests against the real pointer, so an isolated WM_RBUTTONUP is insufficient.
                // Only deliver input after confirming the point belongs to this test-owned HWND.
                var handle = new IntPtr(pet!.Current.NativeWindowHandle);
                var bounds = pet.Current.BoundingRectangle;
                GetCursorPos(out var previousPointer);
                var point = new NativePoint { X = (int)(bounds.Left + bounds.Width / 2), Y = (int)(bounds.Top + bounds.Height / 2) };
                AutomationElement? exit = null;
                try
                {
                    if (!SetCursorPos(point.X, point.Y) || WindowFromPoint(point) != handle) throw new Exception("Test pet is not available for pointer input.");
                    mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
                    await DesktopPerformance.Until(() =>
                    {
                        exit = Windows(child.Id).Select(w => w.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "退出"), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)))).FirstOrDefault(e => e is not null);
                        return exit is not null;
                    }, 5);
                    ((InvokePattern)exit!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                }
                finally { SetCursorPos(previousPointer.X, previousPointer.Y); }
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); graceful = child.ExitCode == 0;
                if (!graceful) throw new Exception("Published app exit failed.");
                using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(profile, "novelrag.db")};Mode=ReadOnly;Pooling=False"); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA user_version";
                if ((long)cmd.ExecuteScalar()! != 2) throw new Exception("Published schema upgrade failed.");
                if (legacy && Directory.GetFiles(profile, "*.pre-migrate-*.bak").Length != 1) throw new Exception("Published upgrade missing safety backup.");
            }
            finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
            await File.WriteAllTextAsync(Path.Combine(profile, "published-result.json"), JsonSerializer.Serialize(new { Passed = true, Legacy = legacy, GracefulExit = graceful, Checks = new[] { "self-contained process with invalid DOTNET_ROOT and restricted PATH", "pet window ready", "second instance forwards", "capture closes while host remains", "UI exit stops owned host", "schema2", "v1 safety backup when upgrading" } }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private static IEnumerable<AutomationElement> Windows(int processId) => AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, processId)).Cast<AutomationElement>();
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
