using System.Numerics;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class SixLaborsFontSourceTests
{
    private static readonly string[] FontNames = ["DejaVuSans.ttf", "DejaVuSansMono.ttf", "DejaVuSerif.ttf"];

    public static TheoryData<string> AllFonts => new(FontNames);

    private static readonly Dictionary<string, SixLaborsFontSource> Fonts =
        FontNames.ToDictionary(name => name, name => new SixLaborsFontSource(TestPaths.Font(name)));

    private static SixLaborsFontSource Font(string name) => Fonts[name];

    private static IEnumerable<Vector2> Points(Glyph glyph) =>
        glyph.Contours.SelectMany(c => c.Segments.Select(s => s.End).Prepend(c.Start));

    // --- Behaviour every font must satisfy ---

    [Theory]
    [MemberData(nameof(AllFonts))]
    public void Reads_font_metrics(string font)
    {
        var m = Font(font).Metrics;

        Assert.Equal(2048, m.UnitsPerEm);
        Assert.InRange(m.Ascender, 1, m.UnitsPerEm);
        Assert.InRange(m.Descender, -m.UnitsPerEm, -1);
    }

    [Theory]
    [MemberData(nameof(AllFonts))]
    public void H_is_one_contour_sitting_on_the_baseline_with_y_up(string font)
    {
        var h = Font(font).GetGlyph('H');

        Assert.Single(h.Contours);
        Assert.True(h.AdvanceWidth > 0);

        var points = Points(h).ToList();
        Assert.Equal(0, points.Min(p => p.Y));
        Assert.InRange(points.Max(p => p.Y), 1, Font(font).Metrics.Ascender);
        Assert.Equal(h.LeftSideBearing, points.Min(p => p.X));
        Assert.True(points.Max(p => p.X) <= h.AdvanceWidth);
    }

    [Theory]
    [MemberData(nameof(AllFonts))]
    public void O_has_outer_and_inner_contours_built_from_curves(string font)
    {
        var o = Font(font).GetGlyph('o');

        Assert.Equal(2, o.Contours.Count);
        Assert.Contains(o.Contours.SelectMany(c => c.Segments), s => s is QuadSegment or CubicSegment);
    }

    [Theory]
    [MemberData(nameof(AllFonts))]
    public void P_descends_below_the_baseline_but_not_past_the_descender(string font)
    {
        var minY = Points(Font(font).GetGlyph('p')).Min(pt => pt.Y);

        Assert.InRange(minY, Font(font).Metrics.Descender, -1);
    }

    [Theory]
    [MemberData(nameof(AllFonts))]
    public void Space_advances_but_has_no_outline(string font)
    {
        var space = Font(font).GetGlyph(' ');

        Assert.True(space.AdvanceWidth > 0);
        Assert.Empty(space.Contours);
    }

    [Theory]
    [MemberData(nameof(AllFonts))]
    public void Unmapped_code_points_fall_back_to_notdef(string font)
    {
        foreach (var codepoint in new[] { 0xE000, 0xD800, -1 })  // private use, lone surrogate, invalid
        {
            var glyph = Font(font).GetGlyph(codepoint);

            Assert.Equal(0, glyph.GlyphId);
            Assert.True(glyph.AdvanceWidth > 0);
            Assert.NotEmpty(glyph.Contours);
        }
    }

    [Theory]
    [MemberData(nameof(AllFonts))]
    public void Loads_from_stream(string font)
    {
        using var stream = File.OpenRead(TestPaths.Font(font));

        var source = new SixLaborsFontSource(stream);

        Assert.Equal(Font(font).Metrics, source.Metrics);
        Assert.Equal(Font(font).GetGlyph('A').AdvanceWidth, source.GetGlyph('A').AdvanceWidth);
    }

    // --- Font-specific checks ---

    [Fact]
    public void DejaVuSans_has_known_metrics()
    {
        var font = Font("DejaVuSans.ttf");
        var h = font.GetGlyph('H');

        Assert.Equal(new FontMetrics(2048, 1901, -483, 0), font.Metrics);
        Assert.Equal(1540, h.AdvanceWidth);  // 201 lsb + 1138 ink + 201 rsb
        Assert.Equal(201, h.LeftSideBearing);
        Assert.Equal(1493, Points(h).Max(p => p.Y));
    }

    [Fact]
    public void Mono_font_gives_every_character_the_same_advance()
    {
        var font = Font("DejaVuSansMono.ttf");

        var advances = "iWm .@H".Select(c => font.GetGlyph(c).AdvanceWidth).Distinct();

        Assert.Single(advances);
    }

    [Fact]
    public void Proportional_font_gives_narrow_and_wide_characters_different_advances()
    {
        var font = Font("DejaVuSans.ttf");

        Assert.True(font.GetGlyph('i').AdvanceWidth < font.GetGlyph('W').AdvanceWidth);
    }

    [Fact]
    public void Serif_H_has_more_outline_points_than_sans_H()
    {
        // Serifs add extra corners to the same basic shape.
        var sans = Points(Font("DejaVuSans.ttf").GetGlyph('H')).Count();
        var serif = Points(Font("DejaVuSerif.ttf").GetGlyph('H')).Count();

        Assert.True(serif > sans, $"serif {serif} points, sans {sans} points");
    }
}
