using System.Windows.Shapes;

namespace NovelRAG.Desktop;

public sealed class OwlPetRenderer : IPetRenderer
{
    public Task LoadAsync(string? assetPath, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public void SetScale(double scale) { }
    public void PlayIdle() { }
    public void PlayInteraction(string interaction) { }
    public void Dispose() { }
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
