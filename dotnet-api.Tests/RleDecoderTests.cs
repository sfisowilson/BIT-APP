using Xunit;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests;

public class RleDecoderTests
{
    [Fact]
    public void Decode_EmptyRle_ReturnsAllFalse()
    {
        var mask = RleDecoder.Decode("", 10, 10);
        Assert.NotNull(mask);
        Assert.Equal(10, mask.GetLength(0));
        Assert.Equal(10, mask.GetLength(1));
        // All pixels should be false (background)
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                Assert.False(mask[y, x]);
    }

    [Fact]
    public void Decode_SimpleRectRle_ReturnsCorrectMask()
    {
        // 4x4 image with 2x2 foreground block at (1,1)-(2,2)
        // Row-major pixel sequence: 0,0,0,0, 0,1,1,0, 0,1,1,0, 0,0,0,0
        // RLE runs (starting with bg=0): 5 zeros, 2 ones, 2 zeros, 2 ones, 5 zeros
        var rle = "5 2 2 2 5";
        var mask = RleDecoder.Decode(rle, 4, 4);

        // Background pixels at corners
        Assert.False(mask[0, 0]); Assert.False(mask[0, 3]);
        Assert.False(mask[3, 0]); Assert.False(mask[3, 3]);

        // Foreground block
        Assert.True(mask[1, 1]); Assert.True(mask[1, 2]);
        Assert.True(mask[2, 1]); Assert.True(mask[2, 2]);

        // Row 0: all background
        Assert.False(mask[0, 0]); Assert.False(mask[0, 1]);
        Assert.False(mask[0, 2]); Assert.False(mask[0, 3]);

        // Row 1: bg, fg, fg, bg
        Assert.False(mask[1, 0]);
        Assert.True(mask[1, 1]); Assert.True(mask[1, 2]);
        Assert.False(mask[1, 3]);
    }

    [Fact]
    public void Decode_AllForeground_ReturnsAllTrue()
    {
        // 2x2 all foreground: RLE starts with 0 (bg), so "0 4" (0 bg, 4 fg)
        var rle = "0 4";
        var mask = RleDecoder.Decode(rle, 2, 2);
        Assert.True(mask[0, 0]); Assert.True(mask[0, 1]);
        Assert.True(mask[1, 0]); Assert.True(mask[1, 1]);
    }

    [Fact]
    public void MaskToPolygon_RectangleMask_ReturnsContour()
    {
        // 10x10 mask with a 4x4 foreground block at (3,3)-(6,6)
        var mask = new bool[10, 10];
        for (int y = 3; y <= 6; y++)
            for (int x = 3; x <= 6; x++)
                mask[y, x] = true;

        var polygon = RleDecoder.MaskToPolygon(mask);
        Assert.NotEmpty(polygon);
        Assert.True(polygon.Count >= 8); // At least the 4 corners
    }

    [Fact]
    public void MaskToPolygon_EmptyMask_ReturnsEmpty()
    {
        var mask = new bool[10, 10];
        var polygon = RleDecoder.MaskToPolygon(mask);
        Assert.Empty(polygon);
    }

    [Fact]
    public void MaskToPolygon_SinglePixel_ReturnsSinglePoint()
    {
        var mask = new bool[10, 10];
        mask[5, 5] = true;
        var polygon = RleDecoder.MaskToPolygon(mask);
        Assert.NotEmpty(polygon);
    }

    [Fact]
    public void PolygonToJson_Rectangle_ReturnsValidJson()
    {
        var polygon = new List<(int, int)> { (0, 0), (10, 0), (10, 10), (0, 10) };
        var json = RleDecoder.PolygonToJson(polygon);
        Assert.Contains("\"x\":0", json);
        Assert.Contains("\"y\":10", json);
        Assert.StartsWith("[", json);
        Assert.EndsWith("]", json);
    }

    [Fact]
    public void PolygonToJson_Empty_ReturnsEmptyArray()
    {
        var json = RleDecoder.PolygonToJson(new List<(int, int)>());
        Assert.Equal("[]", json);
    }

    [Fact]
    public void PolygonBounds_Rectangle_ReturnsCorrectBounds()
    {
        var polygon = new List<(int, int)> { (10, 20), (100, 20), (100, 80), (10, 80) };
        var bounds = RleDecoder.PolygonBounds(polygon);
        Assert.Equal(10, bounds.xMin);
        Assert.Equal(20, bounds.yMin);
        Assert.Equal(100, bounds.xMax);
        Assert.Equal(80, bounds.yMax);
    }

    [Fact]
    public void PolygonBounds_Empty_ReturnsZeros()
    {
        var bounds = RleDecoder.PolygonBounds(new List<(int, int)>());
        Assert.Equal(0, bounds.xMin);
        Assert.Equal(0, bounds.yMin);
        Assert.Equal(0, bounds.xMax);
        Assert.Equal(0, bounds.yMax);
    }
}
