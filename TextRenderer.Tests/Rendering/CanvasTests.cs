using System.Drawing;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class CanvasTests
{
    [Fact]
    public void New_canvas_is_filled_with_the_background_and_opaque()
    {
        var canvas = new Canvas(3, 2, Color.FromArgb(10, 20, 30));

        Assert.Equal(3 * 2 * 4, canvas.Pixels.Length);
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 3; x++)
            Assert.Equal(Color.FromArgb(255, 10, 20, 30), canvas.GetPixel(x, y));
    }

    [Fact]
    public void Clear_overwrites_every_pixel()
    {
        var canvas = new Canvas(2, 2, Color.Black);
        canvas.Blend(0, 0, 1, Color.Red);

        canvas.Clear(Color.Blue);

        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(Color.Blue.ToArgb(), canvas.GetPixel(i % 2, i / 2).ToArgb()));
    }

    [Fact]
    public void Clear_ignores_colour_alpha()
    {
        var canvas = new Canvas(1, 1, Color.FromArgb(0, 50, 60, 70));

        Assert.Equal(255, canvas.GetPixel(0, 0).A);
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 128)]   // 127.5 rounds to even
    [InlineData(1f, 255)]
    [InlineData(-1f, 0)]      // clamped
    [InlineData(2f, 255)]     // clamped
    [InlineData(float.NaN, 0)]
    public void Blend_mixes_by_coverage(float coverage, int expected)
    {
        var canvas = new Canvas(1, 1, Color.Black);

        canvas.Blend(0, 0, coverage, Color.White);

        var p = canvas.GetPixel(0, 0);
        Assert.Equal((expected, expected, expected, 255), (p.R, p.G, p.B, p.A));
    }

    [Fact]
    public void Blend_mixes_each_channel_independently()
    {
        var canvas = new Canvas(1, 1, Color.FromArgb(200, 0, 100));

        canvas.Blend(0, 0, 0.25f, Color.FromArgb(0, 200, 100));

        var p = canvas.GetPixel(0, 0);
        Assert.Equal((150, 50, 100), (p.R, p.G, p.B));
    }

    [Fact]
    public void Blending_twice_at_half_coverage_is_over_not_additive()
    {
        var canvas = new Canvas(1, 1, Color.Black);

        canvas.Blend(0, 0, 0.5f, Color.White);
        canvas.Blend(0, 0, 0.5f, Color.White);

        // Over: 1 - (1 - 0.5)^2 = 75% of 255 ≈ 191. Additive would saturate at 255.
        Assert.InRange(canvas.GetPixel(0, 0).R, 190, 192);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public void Blend_outside_the_canvas_is_ignored(int x, int y)
    {
        var canvas = new Canvas(2, 2, Color.Black);

        canvas.Blend(x, y, 1, Color.White);

        Assert.All(canvas.Pixels.Where((_, i) => i % 4 != 3), b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-5, 5)]
    public void Rejects_empty_dimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Canvas(width, height, Color.White));
    }
}
