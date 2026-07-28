using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Decodes SAM3 video-rle Run-Length Encoded (RLE) masks into usable formats.
/// RLE format: Kaggle/COCO order — counts alternate between runs of 0s and 1s,
/// starting with 0 (background). The decoded bool[,] has true = foreground, false = background.
/// </summary>
public static class RleDecoder
{
    /// <summary>
    /// Decode a COCO/Kaggle RLE string into a 2D boolean mask.
    /// </summary>
    /// <param name="rle">RLE string in Kaggle/COCO order (space-separated integer counts).</param>
    /// <param name="width">Mask width in pixels.</param>
    /// <param name="height">Mask height in pixels.</param>
    /// <returns>2D bool array: true = foreground (mask), false = background.</returns>
    public static bool[,] Decode(string rle, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(rle))
            return new bool[height, width];

        var counts = rle.Trim().Split(' ')
            .Select(s => int.Parse(s))
            .ToArray();

        var mask = new bool[height, width];
        int pixel = 0;
        bool value = false; // RLE starts with 0 (background)

        foreach (var count in counts)
        {
            for (int i = 0; i < count && pixel < width * height; i++)
            {
                int y = pixel / width;
                int x = pixel % width;
                mask[y, x] = value;
                pixel++;
            }
            value = !value; // Flip after each run
        }

        return mask;
    }

    /// <summary>
    /// Convert a 2D boolean mask to a polygon (list of boundary points) using contour tracing.
    /// Returns the outermost contour as a list of {x, y} points in pixel coordinates.
    /// Uses Moore-Neighbor tracing algorithm.
    /// </summary>
    public static List<(int x, int y)> MaskToPolygon(bool[,] mask)
    {
        int height = mask.GetLength(0);
        int width = mask.GetLength(1);

        // Find the first foreground pixel (top-leftmost)
        int startX = -1, startY = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (mask[y, x])
                {
                    startX = x;
                    startY = y;
                    break;
                }
            }
            if (startX >= 0) break;
        }

        if (startX < 0) return new List<(int, int)>(); // No foreground pixels

        // Moore-Neighbor tracing: 8-connected clockwise from east
        // Directions: E, SE, S, SW, W, NW, N, NE
        int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
        int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

        var boundary = new List<(int x, int y)>();
        int cx = startX, cy = startY;
        int dir = 0; // Start looking east

        // Find first boundary direction from start
        for (int d = 0; d < 8; d++)
        {
            int nx = cx + dx[(7 + d) % 8];
            int ny = cy + dy[(7 + d) % 8];
            if (IsForeground(mask, nx, ny, width, height))
            {
                dir = (7 + d) % 8;
                break;
            }
        }

        int count = 0;
        int maxSteps = width * height * 2; // Safety limit
        do
        {
            boundary.Add((cx, cy));

            // Try to find the next boundary pixel, scanning clockwise from (dir + 5) mod 8
            int searchStart = (dir + 5) % 8;
            bool found = false;
            for (int d = 0; d < 8; d++)
            {
                int nd = (searchStart + d) % 8;
                int nx = cx + dx[nd];
                int ny = cy + dy[nd];
                if (IsForeground(mask, nx, ny, width, height))
                {
                    cx = nx;
                    cy = ny;
                    dir = nd;
                    found = true;
                    break;
                }
            }

            if (!found) break; // Isolated pixel

            count++;
        } while ((cx != startX || cy != startY) && count < maxSteps);

        return boundary;
    }

    /// <summary>
    /// Convert a polygon (list of {x,y} points) to a JSON string suitable for BoundaryCoordinatesJson.
    /// </summary>
    public static string PolygonToJson(List<(int x, int y)> polygon)
    {
        if (polygon.Count == 0) return "[]";

        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < polygon.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('{');
            sb.Append($"\"x\":{polygon[i].x},\"y\":{polygon[i].y}");
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Compute the bounding box of a polygon. Returns (x_min, y_min, x_max, y_max).
    /// </summary>
    public static (int xMin, int yMin, int xMax, int yMax) PolygonBounds(List<(int x, int y)> polygon)
    {
        if (polygon.Count == 0) return (0, 0, 0, 0);
        int xMin = polygon.Min(p => p.x);
        int yMin = polygon.Min(p => p.y);
        int xMax = polygon.Max(p => p.x);
        int yMax = polygon.Max(p => p.y);
        return (xMin, yMin, xMax, yMax);
    }

    private static bool IsForeground(bool[,] mask, int x, int y, int width, int height)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return false;
        return mask[y, x];
    }
}
