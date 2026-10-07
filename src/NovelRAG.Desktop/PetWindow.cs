using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace NovelRAG.Desktop;

public sealed class PetWindow : Window
{
    private readonly AppServices services;
    private readonly DesktopController controller;
    private Point down;
    private bool dragged;
    private readonly Func<IPetRenderer> factory;
    private IPetRenderer? renderer;
    private CancellationTokenSource? loading;
    private int generation;
    private bool closed;
    public Task RendererTask { get; private set; } = Task.CompletedTask;
    public PetWindow(AppServices services, DesktopController controller, Func<IPetRenderer> factory)
    {
        this.services = services; this.controller = controller; this.factory = factory;
        Title = "NovelRAG 灵感桌宠"; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Width = 110 * services.Settings.PetScale; Height = 120 * services.Settings.PetScale;
        Cursor = Cursors.Hand; ToolTip = "点击记录灵感 · 拖动移动 · 右键打开菜单";
        SourceInitialized += (_, _) => RestorePosition();
        IsVisibleChanged += (_, _) => QueueRendererChange();
        Closed += (_, _) => { closed = true; QueueRendererChange(); };
        MouseLeftButtonDown += (_, e) => { down = PointToScreen(e.GetPosition(this)); dragged = false; CaptureMouse(); e.Handled = true; };
        MouseMove += async (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragged || !IsMouseCaptured) return;
            var point = PointToScreen(e.GetPosition(this)); if ((point - down).Length < 5) return;
            dragged = true; ReleaseMouseCapture();
            try { Interact("drag"); DragMove(); await PersistPositionAsync(); } catch (Exception ex) { services.Log.Write("pet.move_failed", ex); }
        };
        MouseLeftButtonUp += (_, e) => { ReleaseMouseCapture(); if (!dragged) { Interact("click"); controller.OpenCapture(); } e.Handled = true; };
        MouseRightButtonUp += (_, e) => { Interact("menu"); ContextMenu = controller.CreatePetMenu(); ContextMenu.IsOpen = true; e.Handled = true; };
    }
    private void Interact(string interaction)
    {
        try { renderer?.PlayInteraction(interaction); renderer?.PlayIdle(); }
        catch (Exception ex) { services.Log.Write("pet.interaction_failed", ex); }
    }
    private void QueueRendererChange()
    {
        loading?.Cancel(); var next = ++generation;
        RendererTask = ChangeRendererAsync(RendererTask, next);
    }
    private void Release(IPetRenderer? value)
    {
        try { value?.Dispose(); } catch (Exception ex) { services.Log.Write("pet.dispose_failed", ex); }
    }
    private async Task ChangeRendererAsync(Task previous, int requested)
    {
        await previous;
        if (requested != generation) return;
        Content = null; Release(renderer); renderer = null;
        if (closed || !IsVisible) return;
        using var cts = new CancellationTokenSource(); loading = cts;
        IPetRenderer? candidate = null;
        void Install()
        {
            var visual = candidate!.CreateVisual(); candidate.SetScale(services.Settings.PetScale); candidate.PlayIdle();
            Content = visual; renderer = candidate; candidate = null;
        }
        try
        {
            candidate = factory(); await candidate.LoadAsync(null, cts.Token); cts.Token.ThrowIfCancellationRequested();
            Install();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            services.Log.Write("pet.load_failed", ex); Release(candidate); candidate = null;
            if (!cts.IsCancellationRequested && requested == generation && !closed && IsVisible)
            {
                candidate = new OwlPetRenderer(); await candidate.LoadAsync(null, cts.Token); Install();
            }
        }
        finally { Release(candidate); loading = null; }
    }
    public void ApplySettings()
    {
        Width = 110 * services.Settings.PetScale; Height = 120 * services.Settings.PetScale;
        try { renderer?.SetScale(services.Settings.PetScale); } catch (Exception ex) { services.Log.Write("pet.scale_failed", ex); }
        if (services.Settings.PetVisible) { Show(); RestorePosition(); } else Hide();
    }
    private void RestorePosition()
    {
        var settings = services.Settings;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.MonitorName) ?? Forms.Screen.PrimaryScreen!;
        var area = screen.WorkingArea;
        MoveTo(settings.PetX ?? area.Right - 160, settings.PetY ?? area.Bottom - 180, screen);
    }
    private void MoveTo(double x, double y, Forms.Screen screen)
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        var dpi = GetDpiForWindow(handle) / 96d;
        var a = screen.WorkingArea;
        var p = PetGeometry.Place(x, y, Width * dpi, Height * dpi, new(a.X, a.Y, a.Width, a.Height), services.Settings.EdgeSnap);
        SetWindowPos(handle, IntPtr.Zero, (int)p.X, (int)p.Y, 0, 0, 0x0015);
    }
    public async Task PersistPositionAsync()
    {
        var handle = new WindowInteropHelper(this).Handle;
        GetWindowRect(handle, out var rect); var screen = Forms.Screen.FromHandle(handle);
        MoveTo(rect.Left, rect.Top, screen); GetWindowRect(handle, out rect);
        await services.UpdateSettingsAsync(s => s with { PetX = rect.Left, PetY = rect.Top, MonitorName = screen.DeviceName });
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
