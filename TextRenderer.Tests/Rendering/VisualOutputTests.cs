using System.Drawing;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

/// <summary>Writes images to <c>out/</c> for checking by eye. They also fail if writing throws.</summary>
public class VisualOutputTests
{
    [Fact]
    public void Writes_filled_rectangle_ppm()
    {
        var canvas = new Canvas(120, 80, Color.White);

        // Solid blue rectangle.
        for (var y = 20; y < 60; y++)
        for (var x = 20; x < 70; x++)
            canvas.Blend(x, y, 1, Color.RoyalBlue);

        // A vertical coverage ramp to the right, to see blending: transparent at the top, solid at the bottom.
        for (var y = 10; y < 70; y++)
        for (var x = 80; x < 110; x++)
            canvas.Blend(x, y, (y - 10) / 59f, Color.Crimson);

        var path = TestPaths.Output("test.ppm");
        File.WriteAllBytes(path, new PpmEncoder().Encode(canvas));

        Assert.True(new FileInfo(path).Length > 0);
    }

    [Theory]
    [InlineData('H')]
    [InlineData('o')]
    public void Writes_single_glyph_ppm(char character)
    {
        const float fontSize = 96;
        const int padding = 8;
        var font = new SixLaborsFontSource(TestPaths.Font("DejaVuSans.ttf"));
        var glyph = font.GetGlyph(character);
        var scale = fontSize / font.Metrics.UnitsPerEm;

        // Minimal stand-in for TextLayout (step 4): one glyph, pen at the left padding.
        var width = (int)MathF.Ceiling(glyph.AdvanceWidth * scale) + 2 * padding;
        var height = (int)MathF.Ceiling((font.Metrics.Ascender - font.Metrics.Descender) * scale) + 2 * padding;
        var baseline = padding + font.Metrics.Ascender * scale;

        var canvas = new Canvas(width, height, Color.White);
        var edges = PathFlattener.Flatten(glyph, scale, padding, baseline);
        canvas.Blend(Rasterizer.Rasterize(edges, width, height), Color.Black);

        var path = TestPaths.Output($"glyph-{character}.ppm");
        File.WriteAllBytes(path, new PpmEncoder().Encode(canvas));

        Assert.True(new FileInfo(path).Length > 0);
    }
}
