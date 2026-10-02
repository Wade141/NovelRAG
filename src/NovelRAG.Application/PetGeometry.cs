using NovelRAG.Domain;

namespace NovelRAG.Application;

public static class PetGeometry
{
    public static (double X, double Y) Place(double x, double y, double width, double height, ScreenRect area, bool snap)
    {
        var right = area.X + Math.Max(0, area.Width - width); var bottom = area.Y + Math.Max(0, area.Height - height);
        x = Math.Clamp(x, area.X, right); y = Math.Clamp(y, area.Y, bottom);
        if (snap)
        {
            if (x - area.X < 24) x = area.X; else if (right - x < 24) x = right;
            if (y - area.Y < 24) y = area.Y; else if (bottom - y < 24) y = bottom;
        }
        return (x, y);
    }
}
