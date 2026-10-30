using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

/// <summary>
/// Facts any correct render must satisfy, checked against the font data itself rather than reference images.
/// Renders are black on white, so coverage = (255 − R) / 255.
/// </summary>
public class ImageInvariantTests
{
    private static readonly Dictionary<string, SixLaborsFontSource> Fonts = new();

    private static SixLaborsFontSource Font(string file)
    {
        lock (Fonts)
        {
            if (!Fonts.TryGetValue(file, out var font))
                Fonts[file] = font = new SixLaborsFontSource(TestPaths.Font(file));
            return font;
        }
    }

    private static float Coverage(Canvas canvas, int x, int y) => (255 - canvas.GetPixel(x, y).R) / 255f;

    private static (Canvas Canvas, LayoutResult Layout) Render(string fontFile, string text, float size)
    {
        var options = new RenderOptions { FontSize = size };
        var font = Font(fontFile);
        return (new RenderPipeline(font).Render(text, options), TextLayout.Layout(text, font, options));
    }

    /// <summary>Pixel-space edges of the glyph at <paramref name="index"/>, exactly as the pipeline places it.</summary>
    private static List<Edge> EdgesOf(LayoutResult layout, int index)
    {
        var (glyph, penX) = layout.Glyphs[index];
        return PathFlattener.Flatten(glyph, layout.Scale, penX, layout.Baseline);
    }

    private static (int Left, int Top, int Right, int Bottom)? InkBounds(Canvas canvas)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < canvas.Height; y++)
        for (var x = 0; x < canvas.Width; x++)
        {
            if (Coverage(canvas, x, y) <= 0)
                continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        return right < 0 ? null : (left, top, right, bottom);
    }

    public static TheoryData<string, char> AreaCases()
    {
        var data = new TheoryData<string, char>();
        foreach (var font in new[] { "DejaVuSans.ttf", "DejaVuSansMono.ttf", "DejaVuSerif.ttf" })
        foreach (var c in "Ho@&gWS8%")
            data.Add(font, c);
        return data;
    }

    [Theory]
    [MemberData(nameof(AreaCases))]
    public void Ink_area_matches_outline_area(string fontFile, char character)
    {
        var (canvas, layout) = Render(fontFile, character.ToString(), 64);

        // Shoelace formula over the flattened outline. Holes run the opposite way, so they subtract.
        var area = MathF.Abs(EdgesOf(layout, 0).Sum(e => e.X0 * e.Y1 - e.X1 * e.Y0) / 2);

        var ink = 0f;
        for (var y = 0; y < canvas.Height; y++)
        for (var x = 0; x < canvas.Width; x++)
            ink += Coverage(canvas, x, y);

        Assert.InRange(ink, area * 0.98f, area * 1.02f);
    }

    [Theory]
    [MemberData(nameof(AreaCases))]
    public void Ink_bounding_box_matches_outline_bounding_box(string fontFile, char character)
    {
        var (canvas, layout) = Render(fontFile, character.ToString(), 64);
        var edges = EdgesOf(layout, 0);

        var ink = InkBounds(canvas)!.Value;

        // Ink can only start in the pixel containing the outline's extreme, or the one next to it
        // (if the outline only just enters a pixel, no sample may land inside it).
        Assert.InRange(ink.Left, (int)MathF.Floor(edges.Min(e => MathF.Min(e.X0, e.X1))), (int)MathF.Floor(edges.Min(e => MathF.Min(e.X0, e.X1))) + 1);
        Assert.InRange(ink.Right, (int)MathF.Ceiling(edges.Max(e => MathF.Max(e.X0, e.X1))) - 2, (int)MathF.Ceiling(edges.Max(e => MathF.Max(e.X0, e.X1))) - 1);
        Assert.InRange(ink.Top, (int)MathF.Floor(edges.Min(e => MathF.Min(e.Y0, e.Y1))), (int)MathF.Floor(edges.Min(e => MathF.Min(e.Y0, e.Y1))) + 1);
        Assert.InRange(ink.Bottom, (int)MathF.Ceiling(edges.Max(e => MathF.Max(e.Y0, e.Y1))) - 2, (int)MathF.Ceiling(edges.Max(e => MathF.Max(e.Y0, e.Y1))) - 1);
    }

    [Theory]
    [InlineData("DejaVuSans.ttf", 'o')]
    [InlineData("DejaVuSans.ttf", 'O')]
    [InlineData("DejaVuSans.ttf", '0')]
    [InlineData("DejaVuSerif.ttf", 'o')]
    [InlineData("DejaVuSerif.ttf", 'O')]
    [InlineData("DejaVuSansMono.ttf", 'o')]
    public void Holes_are_empty(string fontFile, char character)
    {
        var (canvas, _) = Render(fontFile, character.ToString(), 64);
        var ink = InkBounds(canvas)!.Value;

        var centre = Coverage(canvas, (ink.Left + ink.Right) / 2, (ink.Top + ink.Bottom) / 2);

        Assert.Equal(0f, centre);
    }

    [Theory]
    [InlineData("DejaVuSans.ttf")]
    [InlineData("DejaVuSansMono.ttf")]
    [InlineData("DejaVuSerif.ttf")]
    public void Flat_bottomed_letters_have_no_ink_below_the_baseline(string fontFile)
    {
        // Not "N": DejaVu Serif's N dips 29 units below the baseline at the foot of its diagonal, by design.
        var (canvas, layout) = Render(fontFile, "xzHk", 64);

        // The baseline row itself can be partly covered; every row fully below it must be empty.
        for (var y = (int)MathF.Ceiling(layout.Baseline); y < canvas.Height; y++)
        for (var x = 0; x < canvas.Width; x++)
            Assert.True(Coverage(canvas, x, y) == 0, $"ink at ({x},{y}), baseline {layout.Baseline}");
    }

    [Theory]
    [InlineData("DejaVuSans.ttf", 'p')]
    [InlineData("DejaVuSans.ttf", 'g')]
    [InlineData("DejaVuSerif.ttf", 'y')]
    [InlineData("DejaVuSansMono.ttf", 'q')]
    public void Descenders_have_ink_well_below_the_baseline(string fontFile, char character)
    {
        var (canvas, layout) = Render(fontFile, character.ToString(), 64);

        var bottom = InkBounds(canvas)!.Value.Bottom;

        Assert.True(bottom > layout.Baseline + 8, $"ink bottom {bottom}, baseline {layout.Baseline}");
    }

    [Fact]
    public void Repeated_glyph_is_an_exact_copy_shifted_by_its_advance()
    {
        // 1540 units × 102.4 / 2048 = exactly 77px, so the second H lands on the same sub-pixel offset.
        var (canvas, layout) = Render("DejaVuSans.ttf", "HH", 102.4f);
        var shift = (int)(layout.Glyphs[1].PenX - layout.Glyphs[0].PenX);
        Assert.Equal(77, shift);

        for (var y = 0; y < canvas.Height; y++)
        for (var x = 0; x < (int)layout.Glyphs[1].PenX; x++)
            Assert.Equal(canvas.GetPixel(x, y), canvas.GetPixel(x + shift, y));
    }

    [Theory]
    [InlineData("DejaVuSans.ttf")]
    [InlineData("DejaVuSerif.ttf")]
    public void Spaces_render_no_ink(string fontFile)
    {
        var (canvas, _) = Render(fontFile, "     ", 48);

        Assert.Null(InkBounds(canvas));
    }

    [Theory]
    [InlineData("DejaVuSans.ttf", "Ångström fj")]
    [InlineData("DejaVuSansMono.ttf", "Ångström fj")]
    [InlineData("DejaVuSerif.ttf", "Ångström fj")]
    [InlineData("DejaVuSerif.ttf", "j")]    // negative left bearing on the first glyph
    [InlineData("DejaVuSerif.ttf", "fff")]  // overhang past the last advance
    public void Zero_padding_clips_nothing(string fontFile, string text)
    {
        static float TotalInk(string fontFile, string text, int padding)
        {
            var canvas = new RenderPipeline(Font(fontFile)).Render(text, new RenderOptions { FontSize = 64, Padding = padding });
            var ink = 0f;
            for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
                ink += Coverage(canvas, x, y);
            return ink;
        }

        Assert.Equal(TotalInk(fontFile, text, 8), TotalInk(fontFile, text, 0), 0.01f);
    }

    [Fact]
    public void Same_input_gives_byte_identical_png()
    {
        var encoder = new PngEncoder();

        var first = encoder.Encode(Render("DejaVuSerif.ttf", "Determinism @ 3.14", 37.5f).Canvas);
        var second = encoder.Encode(Render("DejaVuSerif.ttf", "Determinism @ 3.14", 37.5f).Canvas);

        Assert.Equal(first, second);
    }
}
