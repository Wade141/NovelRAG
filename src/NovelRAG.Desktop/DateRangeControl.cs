using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace NovelRAG.Desktop;

public sealed class DateRangeControl : UserControl
{
    private readonly TextBox start = new(), end = new();
    private readonly TextBlock error = Ui.Text("", 12, Brushes.Firebrick);
    public event Action? Changed;

    public DateRangeControl()
    {
        var panel = new StackPanel();
        var heading = new DockPanel();
        var clear = new Button { Content = "清空日期", Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(6, 0, 0, 4) };
        AutomationProperties.SetAutomationId(clear, "ClearDateRange");
        clear.Click += (_, _) => { start.Clear(); end.Clear(); HideError(); };
        DockPanel.SetDock(clear, Dock.Right); heading.Children.Add(clear); heading.Children.Add(Ui.Text("创建日期 · 含起止当天", 12, Ui.Muted)); panel.Children.Add(heading);
        var fields = new WrapPanel(); fields.Children.Add(Field(start, "开始", "DateRangeStart")); fields.Children.Add(Field(end, "结束", "DateRangeEnd")); panel.Children.Add(fields);
        AutomationProperties.SetAutomationId(error, "DateRangeError"); error.Visibility = Visibility.Collapsed; panel.Children.Add(error);
        Content = panel;
        start.TextChanged += (_, _) => OnChanged(); end.TextChanged += (_, _) => OnChanged();
    }

    private FrameworkElement Field(TextBox input, string label, string id)
    {
        var field = new StackPanel { Width = 133, Margin = new Thickness(0, 0, 6, 4) };
        field.Children.Add(Ui.Text(label, 12, Ui.Muted));
        var row = new DockPanel();
        var button = new Button { Content = "▦", Width = 26, ToolTip = "选择" + label + "日期" };
        AutomationProperties.SetAutomationId(button, id + "Calendar"); AutomationProperties.SetName(button, "选择" + label + "日期");
        DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
        input.Padding = new Thickness(4); input.ToolTip = "yyyy-MM-dd；留空表示不限";
        AutomationProperties.SetAutomationId(input, id); AutomationProperties.SetName(input, label + "日期（yyyy-MM-dd）"); row.Children.Add(input); field.Children.Add(row);
        var calendar = new System.Windows.Controls.Calendar();
        var popup = new Popup { Child = calendar, PlacementTarget = input, Placement = PlacementMode.Bottom, StaysOpen = false };
        button.Click += (_, _) =>
        {
            if (popup.IsOpen) { popup.IsOpen = false; return; }
            calendar.SelectedDate = null;
            calendar.DisplayDate = DateOnly.TryParseExact(input.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToDateTime(TimeOnly.MinValue) : DateTime.Today;
            popup.IsOpen = true; calendar.Focus();
        };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDate is not { } selected) return;
            input.Text = selected.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); popup.IsOpen = false; input.Focus();
        };
        calendar.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { popup.IsOpen = false; input.Focus(); e.Handled = true; } };
        Unloaded += (_, _) => popup.IsOpen = false;
        return field;
    }

    public bool TryGetUtcBounds(out DateTimeOffset? from, out DateTimeOffset? to)
    {
        from = to = null;
        try
        {
            (from, to) = DateRangeFilter.ToUtcBounds(Parse(start.Text, "开始"), Parse(end.Text, "结束"), TimeZoneInfo.Local);
            HideError(); return true;
        }
        catch (ArgumentException ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; return false; }
    }

    private static DateOnly? Parse(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)) return value;
        throw new ArgumentException(label + "日期无效，请按 yyyy-MM-dd 输入或从日历选择。");
    }
    private void OnChanged() { HideError(); Changed?.Invoke(); }
    private void HideError() { error.Text = ""; error.Visibility = Visibility.Collapsed; }
}
