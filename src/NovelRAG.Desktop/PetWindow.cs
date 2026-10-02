using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace NovelRAG.Desktop;

public interface IPetRenderer { FrameworkElement CreateVisual(); }
public sealed class OwlPetRenderer : IPetRenderer
{
    public FrameworkElement CreateVisual()
    {
        var canvas = new Canvas { Width = 110, Height = 120, Background = Brushes.Transparent };
        void Add(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); canvas.Children.Add(e); }
        Add(new Ellipse { Width = 76, Height = 14, Fill = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0)) }, 17, 101);
        Add(new Polygon { Points = new PointCollection { new(16, 40), new(20, 8), new(45, 30), new(75, 30), new(92, 8), new(98, 46) }, Fill = Ui.Accent }, 0, 0);
        Add(new Ellipse { Width = 88, Height = 88, Fill = Ui.Accent }, 12, 22);
        Add(new Ellipse { Width = 68, Height = 66, Fill = new SolidColorBrush(Color.FromRgb(239, 231, 207)) }, 22, 37);
        foreach (var x in new[] { 28d, 61d })
        {
            Add(new Ellipse { Width = 26, Height = 28, Fill = Brushes.White }, x, 38);
            Add(new Ellipse { Width = 9, Height = 13, Fill = Ui.Ink }, x + 9, 45);
            Add(new Ellipse { Width = 3, Height = 4, Fill = Brushes.White }, x + 10, 46);
        }
        Add(new Polygon { Points = new PointCollection { new(0, 0), new(12, 0), new(6, 10) }, Fill = Brushes.DarkGoldenrod }, 50, 65);
        Add(new Border { Width = 54, Height = 24, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromRgb(196, 146, 91)), Child = new TextBlock { Text = "灵感", FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }, 29, 83);
        return new Viewbox { Child = canvas };
    }
}
public sealed class PetWindow : Window
{
    private readonly AppServices services;
    private readonly DesktopController controller;
    private Point down;
    private bool dragged;
    public PetWindow(AppServices services, DesktopController controller, IPetRenderer renderer)
    {
        this.services = services; this.controller = controller;
        Title = "NovelRAG 灵感桌宠"; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Width = 110 * services.Settings.PetScale; Height = 120 * services.Settings.PetScale;
        Content = renderer.CreateVisual(); Cursor = Cursors.Hand; ToolTip = "点击记录灵感 · 拖动移动 · 右键打开菜单";
        SourceInitialized += (_, _) => RestorePosition();
        MouseLeftButtonDown += (_, e) => { down = PointToScreen(e.GetPosition(this)); dragged = false; CaptureMouse(); e.Handled = true; };
        MouseMove += async (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragged || !IsMouseCaptured) return;
            var point = PointToScreen(e.GetPosition(this)); if ((point - down).Length < 5) return;
            dragged = true; ReleaseMouseCapture();
            try { DragMove(); await PersistPositionAsync(); } catch (Exception ex) { services.Log.Write("pet.move_failed", ex); }
        };
        MouseLeftButtonUp += (_, e) => { ReleaseMouseCapture(); if (!dragged) controller.OpenCapture(); e.Handled = true; };
        MouseRightButtonUp += (_, e) => { ContextMenu = controller.CreatePetMenu(); ContextMenu.IsOpen = true; e.Handled = true; };
    }
    public void ApplySettings()
    {
        Width = 110 * services.Settings.PetScale; Height = 120 * services.Settings.PetScale;
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
