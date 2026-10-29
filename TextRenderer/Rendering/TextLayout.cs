using System.Text;

namespace TextRenderer.Rendering;

/// <summary>A glyph and the pixel x of its pen position.</summary>
public readonly record struct PositionedGlyph(Glyph Glyph, float PenX);

/// <param name="Scale">Pixels per font unit.</param>
/// <param name="Baseline">Pixel y of the baseline, from the top of the canvas.</param>
public sealed record LayoutResult(float Scale, float Baseline, int Width, int Height, IReadOnlyList<PositionedGlyph> Glyphs);

/// <summary>Places glyphs on a single line, left to right, using each glyph's advance width.</summary>
public static class TextLayout
{
    public static LayoutResult Layout(string text, IFontSource font, RenderOptions options)
    {
        var metrics = font.Metrics;
        var scale = options.FontSize / metrics.UnitsPerEm;

        var glyphs = new List<PositionedGlyph>();
        var advanceUnits = 0L;  // summed in font units so the pen doesn't accumulate float error
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = font.GetGlyph(rune.Value);
            glyphs.Add(new PositionedGlyph(glyph, options.Padding + advanceUnits * scale));
            advanceUnits += glyph.AdvanceWidth;
        }

        var width = (int)MathF.Ceiling(advanceUnits * scale) + 2 * options.Padding;
        var height = (int)MathF.Ceiling((metrics.Ascender - metrics.Descender) * scale) + 2 * options.Padding;
        var baseline = options.Padding + metrics.Ascender * scale;

        return new LayoutResult(scale, baseline, Math.Max(1, width), Math.Max(1, height), glyphs);
    }
}
