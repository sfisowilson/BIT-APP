using System;
using System.Collections.Generic;
using System.Linq;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Fits the minimum-area bounding rectangle around a polygon (rotating calipers over its
/// convex hull), used to turn an RLE-decoded mask polygon into a 4-corner quad for planar
/// homography warping. Pure C# — this codebase has no OpenCV dependency (see
/// OpenCvCompositingService's own "no native dependency" convention).
/// </summary>
public static class MinAreaRectFitter
{
    /// <summary>
    /// Fit a 4-corner quad (in original pixel coordinates, order: TL, TR, BR, BL relative to
    /// the fitted rectangle's own orientation) around the given polygon. Falls back to the
    /// polygon's axis-aligned bounding box if it has fewer than 3 distinct points.
    /// </summary>
    public static List<(int x, int y)> FitQuad(List<(int x, int y)> polygon)
    {
        var hull = ConvexHull(polygon);

        if (hull.Count < 3)
            return AxisAlignedBoundingBoxQuad(polygon);

        double bestArea = double.MaxValue;
        (double x, double y)[] bestCorners = Array.Empty<(double, double)>();

        for (int i = 0; i < hull.Count; i++)
        {
            var p1 = hull[i];
            var p2 = hull[(i + 1) % hull.Count];

            var edgeX = p2.x - p1.x;
            var edgeY = p2.y - p1.y;
            var edgeLen = Math.Sqrt(edgeX * edgeX + edgeY * edgeY);
            if (edgeLen < 1e-9) continue;

            // Unit axes aligned with this hull edge.
            var ux = edgeX / edgeLen;
            var uy = edgeY / edgeLen;
            var vx = -uy;
            var vy = ux;

            double minU = double.MaxValue, maxU = double.MinValue;
            double minV = double.MaxValue, maxV = double.MinValue;

            foreach (var p in hull)
            {
                var u = p.x * ux + p.y * uy;
                var v = p.x * vx + p.y * vy;
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }

            var area = (maxU - minU) * (maxV - minV);
            if (area < bestArea)
            {
                bestArea = area;
                bestCorners = new (double, double)[]
                {
                    (minU * ux + minV * vx, minU * uy + minV * vy),
                    (maxU * ux + minV * vx, maxU * uy + minV * vy),
                    (maxU * ux + maxV * vx, maxU * uy + maxV * vy),
                    (minU * ux + maxV * vx, minU * uy + maxV * vy),
                };
            }
        }

        if (bestCorners.Length == 0)
            return AxisAlignedBoundingBoxQuad(polygon);

        return bestCorners.Select(c => ((int)Math.Round(c.Item1), (int)Math.Round(c.Item2))).ToList();
    }

    private static List<(int x, int y)> AxisAlignedBoundingBoxQuad(List<(int x, int y)> polygon)
    {
        if (polygon.Count == 0)
            return new List<(int x, int y)> { (0, 0), (0, 0), (0, 0), (0, 0) };

        var xMin = polygon.Min(p => p.x);
        var xMax = polygon.Max(p => p.x);
        var yMin = polygon.Min(p => p.y);
        var yMax = polygon.Max(p => p.y);

        return new List<(int x, int y)>
        {
            (xMin, yMin), (xMax, yMin), (xMax, yMax), (xMin, yMax),
        };
    }

    /// <summary>Andrew's monotone chain convex hull, returned in counter-clockwise order.</summary>
    private static List<(int x, int y)> ConvexHull(List<(int x, int y)> points)
    {
        var pts = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
        if (pts.Count < 3) return pts;

        var lower = new List<(int x, int y)>();
        foreach (var p in pts)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        var upper = new List<(int x, int y)>();
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            var p = pts[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static long Cross((int x, int y) o, (int x, int y) a, (int x, int y) b)
        => (long)(a.x - o.x) * (b.y - o.y) - (long)(a.y - o.y) * (b.x - o.x);
}
