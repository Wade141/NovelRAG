using NovelRAG.Application;
using NovelRAG.Domain;
using Xunit;

namespace NovelRAG.Tests;

public sealed class PetGeometryTests
{
    [Fact]
    public void NegativeMonitorAndEdgeSnapWorkInPhysicalPixels()
    {
        var area = new ScreenRect(-1920, 0, 1920, 1040);
        var point = PetGeometry.Place(-1910, 25, 120, 140, area, true);
        Assert.Equal(-1920, point.X); Assert.Equal(25, point.Y);
        point = PetGeometry.Place(-5000, 2000, 120, 140, area, false);
        Assert.Equal(-1920, point.X); Assert.Equal(900, point.Y);
    }
}
