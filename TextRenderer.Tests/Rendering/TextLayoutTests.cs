using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class TextLayoutTests
{
    // FakeFontSource at size 16: scale 0.25, ascender 12px, descender 4px.
    private static readonly RenderOptions Options = new() { FontSize = 16, Padding = 2 };

    private static LayoutResult Layout(string text, RenderOptions? options = null) =>
        TextLayout.Layout(text, new FakeFontSource(), options ?? Options);

    [Fact]
    public void Scale_is_font_size_over_units_per_em()
    {
        Assert.Equal(0.25f, Layout("A").Scale);
    }

    [Fact]
    public void Pen_positions_are_cumulative_scaled_advances_from_the_padding()
    {
        var layout = Layout("ABA");

        // A = 8px, B = 12px: pens at 2, 2+8, 2+8+12.
        Assert.Equal([2f, 10f, 22f], layout.Glyphs.Select(g => g.PenX));
    }

    [Fact]
    public void Canvas_size_and_baseline_follow_the_formulas()
    {
        var layout = Layout("AB");

        Assert.Equal(8 + 12 + 2 * 2, layout.Width);          // advances + padding both sides
        Assert.Equal(12 + 4 + 2 * 2, layout.Height);         // ascender + |descender| + padding
        Assert.Equal(2 + 12f, layout.Baseline);              // padding + ascender
    }

    [Fact]
    public void Fractional_total_width_rounds_up()
    {
        var layout = Layout("C");  // 8.25px

        Assert.Equal(9 + 2 * 2, layout.Width);
    }

    [Fact]
    public void Pen_does_not_drift_over_many_fractional_advances()
    {
        var layout = Layout(new string('C', 1000));  // 1000 × 8.25px

        Assert.Equal(2 + 999 * 8.25f, layout.Glyphs[^1].PenX);
        Assert.Equal(8250 + 4, layout.Width);
    }

    [Fact]
    public void Spaces_advance_the_pen()
    {
        var layout = Layout("A A");

        Assert.Equal([2f, 10f, 14f], layout.Glyphs.Select(g => g.PenX));
        Assert.Empty(layout.Glyphs[1].Glyph.Contours);
    }

    [Fact]
    public void Empty_string_gives_a_padding_only_canvas()
    {
        var layout = Layout("");

        Assert.Empty(layout.Glyphs);
        Assert.Equal((4, 20), (layout.Width, layout.Height));
    }

    [Fact]
    public void Empty_string_without_padding_is_still_at_least_one_pixel_wide()
    {
        var layout = Layout("", Options with { Padding = 0 });

        Assert.Equal((1, 16), (layout.Width, layout.Height));
    }

    [Fact]
    public void Surrogate_pair_is_one_glyph()
    {
        var font = new FakeFontSource();

        var layout = TextLayout.Layout("A😀B", font, Options);

        Assert.Equal(3, layout.Glyphs.Count);
        Assert.Equal(['A', 0x1F600, 'B'], font.Requested);
        Assert.Equal(0, layout.Glyphs[1].Glyph.GlyphId);  // falls back to .notdef
        Assert.Equal(2 + 8 + 10f, layout.Glyphs[2].PenX);  // notdef advance is 10px
    }

    [Fact]
    public void Doubling_font_size_doubles_glyph_spacing()
    {
        var small = Layout("AB");
        var large = Layout("AB", Options with { FontSize = 32 });

        Assert.Equal(2 * (small.Glyphs[1].PenX - 2), large.Glyphs[1].PenX - 2);
    }

    [Fact]
    public void Real_font_width_is_sum_of_advances()
    {
        var font = new SixLaborsFontSource(TestPaths.Font("DejaVuSansMono.ttf"));
        var options = new RenderOptions { FontSize = 20.48f, Padding = 0 };  // scale 0.01

        var layout = TextLayout.Layout("Hello", font, options);

        var advance = font.GetGlyph('H').AdvanceWidth * layout.Scale;
        Assert.Equal((int)MathF.Ceiling(5 * advance), layout.Width);
        Assert.Equal(4 * advance, layout.Glyphs[^1].PenX, 0.001f);
    }

    private sealed class BoxFont(float minX, float minY, float maxX, float maxY) : IFontSource
    {
        public FontMetrics Metrics { get; } = new(64, 48, -16, 0);
        public bool HasGlyph(int codepoint) => true;
        public Glyph GetGlyph(int codepoint) => new(1, 32, 0,
        [
            new Contour(new(minX, minY), [new LineSegment(new(maxX, minY)), new LineSegment(new(maxX, maxY)), new LineSegment(new(minX, maxY))]),
        ]);
    }

    // Size 16 (scale 0.25), padding 0: the normal canvas is 8×16 with the pen at 0 and the baseline at 12.
    [Theory]
    [InlineData(0, 0, 32, 32, 0f, 12f, 8, 16)]      // inside: unchanged
    [InlineData(-8, 0, 32, 32, 2f, 12f, 10, 16)]    // 2px past the left: pen shifts, canvas grows
    [InlineData(0, 0, 40, 32, 0f, 12f, 10, 16)]     // 2px past the right
    [InlineData(0, 0, 32, 56, 0f, 14f, 8, 18)]      // 2px past the top: baseline shifts
    [InlineData(0, -24, 32, 32, 0f, 12f, 8, 18)]    // 2px past the bottom
    public void Canvas_grows_only_for_ink_that_would_be_clipped(
        float minX, float minY, float maxX, float maxY, float penX, float baseline, int width, int height)
    {
        var layout = TextLayout.Layout("A", new BoxFont(minX, minY, maxX, maxY), new RenderOptions { FontSize = 16, Padding = 0 });

        Assert.Equal((penX, baseline, width, height), (layout.Glyphs[0].PenX, layout.Baseline, layout.Width, layout.Height));
    }
}
