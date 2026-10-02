using System.Windows.Input;
using System.Windows.Automation;

namespace NovelRAG.Desktop;

internal static class Ui
{
    public static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(34, 50, 46));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(103, 116, 111));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(44, 103, 83));
    public static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(247, 248, 243));
    public static void Configure(Window window, string title, double width, double height)
    {
        window.Title = title + " · NovelRAG"; window.Width = width; window.Height = height;
        window.MinWidth = Math.Min(width, 520); window.MinHeight = Math.Min(height, 400);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Background = Paper; window.Foreground = Ink; window.FontFamily = new FontFamily("Microsoft YaHei UI"); window.FontSize = 14;
    }
    public static TextBlock Text(string value, double size = 14, Brush? color = null) => new()
    { Text = value, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 7) };
    public static TextBox Input(string id, bool multiline = false)
    {
        var box = new TextBox
        {
            Padding = new Thickness(10),
            Margin = new Thickness(0, 4, 0, 10),
            BorderBrush = new SolidColorBrush(Color.FromRgb(202, 212, 204)),
            BorderThickness = new Thickness(1),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden
        };
        AutomationProperties.SetAutomationId(box, id); return box;
    }
    public static Button Button(string text, Func<Task> action, bool primary = false)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 4, 8, 4),
            Background = primary ? Accent : Brushes.White,
            Foreground = primary ? Brushes.White : Ink,
            BorderBrush = primary ? Accent : Brushes.LightGray,
            Cursor = Cursors.Hand
        };
        b.Click += async (_, _) => { b.SetCurrentValue(UIElement.IsEnabledProperty, false); try { await action(); } catch (OperationCanceledException) { } catch (Exception ex) { Error(ex); } finally { b.SetCurrentValue(UIElement.IsEnabledProperty, true); } };
        return b;
    }
    public static void Error(Exception ex) => MessageBox.Show(ex is ArgumentException or InvalidOperationException or KeyNotFoundException ? ex.Message : "操作未完成，请检查本地服务、文件权限或日志后重试。\n" + ex.GetType().Name, "NovelRAG", MessageBoxButton.OK, MessageBoxImage.Warning);
    public static bool Confirm(string message) => MessageBox.Show(message, "NovelRAG · 请确认", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    public static StackPanel Row(params UIElement[] elements)
    { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var e in elements) p.Children.Add(e); return p; }
    public static Border Card(UIElement content) => new() { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(220, 226, 218)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(18), Child = content };
    public static string[] ParseTags(string text) => text.Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static string? Prompt(Window owner, string title, string initial = "")
    {
        var w = new Window { Owner = owner, ResizeMode = ResizeMode.NoResize }; Configure(w, title, 480, 220); w.MinHeight = 220;
        var input = Input("Name"); input.Text = initial;
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(Text(title, 18)); panel.Children.Add(input);
        panel.Children.Add(Button("确定", () => { if (!string.IsNullOrWhiteSpace(input.Text)) w.DialogResult = true; return Task.CompletedTask; }, true));
        w.Content = panel; w.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        w.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        return w.ShowDialog() == true ? input.Text.Trim() : null;
    }
}
