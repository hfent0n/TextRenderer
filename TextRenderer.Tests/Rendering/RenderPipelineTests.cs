using System.Drawing;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class RenderPipelineTests
{
    // FakeFontSource at size 16, padding 2: baseline at y = 14, each box glyph is 8px tall (rows 6..13)
    // and inset 1px from both sides of its advance.
    private static readonly RenderOptions Options = new() { FontSize = 16, Padding = 2 };

    private static Canvas Render(string text, RenderOptions? options = null) =>
        new RenderPipeline(new FakeFontSource()).Render(text, options ?? Options);

    private static bool IsInk(Canvas canvas, int x, int y) => canvas.GetPixel(x, y).ToArgb() == Color.Black.ToArgb();

    [Fact]
    public void Canvas_size_matches_the_layout()
    {
        var canvas = Render("AB");

        Assert.Equal((24, 20), (canvas.Width, canvas.Height));
    }

    [Fact]
    public void Draws_each_glyph_at_its_pen_position()
    {
        var canvas = Render("AB");

        // A: pen 2, advance 8 → ink columns 3..8.  B: pen 10, advance 12 → ink columns 11..20.
        for (var y = 0; y < canvas.Height; y++)
        for (var x = 0; x < canvas.Width; x++)
        {
            var expected = y is >= 6 and < 14 && (x is >= 3 and < 9 || x is >= 11 and < 21);
            Assert.True(expected == IsInk(canvas, x, y), $"pixel ({x},{y}) expected {(expected ? "ink" : "background")}");
        }
    }

    [Fact]
    public void Uses_the_foreground_and_background_colours()
    {
        var options = Options with { Foreground = Color.FromArgb(200, 10, 10), Background = Color.FromArgb(10, 10, 200) };

        var canvas = Render("A", options);

        Assert.Equal(Color.FromArgb(200, 10, 10).ToArgb(), canvas.GetPixel(5, 10).ToArgb());  // inside A
        Assert.Equal(Color.FromArgb(10, 10, 200).ToArgb(), canvas.GetPixel(0, 0).ToArgb());   // padding
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("")]
    public void Whitespace_and_empty_text_draw_nothing(string text)
    {
        var canvas = Render(text);

        for (var y = 0; y < canvas.Height; y++)
        for (var x = 0; x < canvas.Width; x++)
            Assert.False(IsInk(canvas, x, y));
    }

    [Fact]
    public void Same_input_renders_identical_pixels()
    {
        Assert.Equal(Render("ABC A").Pixels, Render("ABC A").Pixels);
    }

    [Fact]
    public void Null_text_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Render(null!));
    }

    [Fact]
    public void Real_font_renders_ink_inside_the_canvas_padding()
    {
        var options = new RenderOptions { FontSize = 32, Padding = 4 };

        var canvas = new RenderPipeline(new SixLaborsFontSource(TestPaths.Font("DejaVuSans.ttf"))).Render("Hello", options);

        var ink = new List<(int X, int Y)>();
        for (var y = 0; y < canvas.Height; y++)
        for (var x = 0; x < canvas.Width; x++)
            if (IsInk(canvas, x, y))
                ink.Add((x, y));

        Assert.NotEmpty(ink);
        Assert.True(ink.Min(p => p.X) >= options.Padding);
        Assert.True(ink.Max(p => p.X) < canvas.Width - options.Padding);
        Assert.True(ink.Min(p => p.Y) >= options.Padding);
    }
}
