namespace NovelRAG.Desktop;

public interface IPetRenderer : IDisposable
{
    Task LoadAsync(string? assetPath, CancellationToken ct = default);
    FrameworkElement CreateVisual();
    // The window owns geometric scaling; use this notification for rendering detail only.
    void SetScale(double scale);
    void PlayIdle();
    void PlayInteraction(string interaction);
}
