using System.Collections.Generic;
using System.Linq;
using Xunit;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests;

public class MinAreaRectFitterTests
{
    [Fact]
    public void FitQuad_AxisAlignedRectangle_ReturnsExactCorners()
    {
        var polygon = new List<(int x, int y)> { (10, 10), (110, 10), (110, 60), (10, 60) };

        var quad = MinAreaRectFitter.FitQuad(polygon);

        Assert.Equal(4, quad.Count);
        var xs = quad.Select(c => c.x).ToList();
        var ys = quad.Select(c => c.y).ToList();
        Assert.Equal(10, xs.Min());
        Assert.Equal(110, xs.Max());
        Assert.Equal(10, ys.Min());
        Assert.Equal(60, ys.Max());
    }

    [Fact]
    public void FitQuad_RotatedSquare_ReturnsTighterAreaThanAxisAlignedBox()
    {
        // A square rotated 45°: diamond shape with vertices at (50,0),(100,50),(50,100),(0,50).
        // Its true min-area rect area is 5000 (side ~70.7^2); the axis-aligned bbox is 100x100=10000.
        var polygon = new List<(int x, int y)> { (50, 0), (100, 50), (50, 100), (0, 50) };

        var quad = MinAreaRectFitter.FitQuad(polygon);

        Assert.Equal(4, quad.Count);
        var area = PolygonArea(quad);
        Assert.True(area < 6000, $"Expected a tight rotated fit (~5000), got area={area}");
    }

    [Fact]
    public void FitQuad_DegenerateCollinearPolygon_FallsBackToBoundingBox()
    {
        // All points on a single line — convex hull has < 3 points, so FitQuad must not throw
        // and should fall back to the axis-aligned bounding box.
        var polygon = new List<(int x, int y)> { (0, 0), (10, 0), (20, 0), (30, 0) };

        var quad = MinAreaRectFitter.FitQuad(polygon);

        Assert.Equal(4, quad.Count);
        Assert.Equal(0, quad.Min(c => c.x));
        Assert.Equal(30, quad.Max(c => c.x));
    }

    [Fact]
    public void FitQuad_EmptyPolygon_ReturnsZeroQuad()
    {
        var quad = MinAreaRectFitter.FitQuad(new List<(int x, int y)>());

        Assert.Equal(4, quad.Count);
        Assert.All(quad, c => Assert.Equal((0, 0), c));
    }

    private static double PolygonArea(List<(int x, int y)> pts)
    {
        double area = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var (x1, y1) = pts[i];
            var (x2, y2) = pts[(i + 1) % pts.Count];
            area += x1 * (double)y2 - x2 * (double)y1;
        }
        return System.Math.Abs(area) / 2.0;
    }
}
