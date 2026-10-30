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
    /// <param name="cancellationToken">Checked before each glyph.</param>
    public static LayoutResult Layout(string text, IFontSource font, RenderOptions options, CancellationToken cancellationToken = default)
    {
        var metrics = font.Metrics;
        var scale = options.FontSize / metrics.UnitsPerEm;

        var placed = new List<(Glyph Glyph, long PenUnits)>();
        var advanceUnits = 0L;  // summed in font units so the pen doesn't accumulate float error
        float inkLeft = 0, inkRight = 0, inkTop = 0, inkBottom = 0;  // font units, relative to the line's origin
        foreach (var rune in text.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var glyph = font.GetGlyph(rune.Value);
            placed.Add((glyph, advanceUnits));
            if (glyph.Bounds is { } ink)
            {
                inkLeft = MathF.Min(inkLeft, advanceUnits + ink.MinX);
                inkRight = MathF.Max(inkRight, advanceUnits + ink.MaxX);
                inkTop = MathF.Max(inkTop, ink.MaxY);
                inkBottom = MathF.Min(inkBottom, ink.MinY);
            }
            advanceUnits += glyph.AdvanceWidth;
        }

        var width = (int)MathF.Ceiling(advanceUnits * scale) + 2 * options.Padding;
        var height = (int)MathF.Ceiling((metrics.Ascender - metrics.Descender) * scale) + 2 * options.Padding;
        var baseline = options.Padding + metrics.Ascender * scale;

        // Ink can overhang the advances and the font's vertical metrics. Padding absorbs that first;
        // the canvas only grows, by whole pixels, for ink that would otherwise be clipped.
        var growLeft = Overflow(-inkLeft * scale - options.Padding);
        var growTop = Overflow(inkTop * scale - baseline);
        width = Math.Max(width, (int)MathF.Ceiling(options.Padding + inkRight * scale)) + growLeft;
        height = Math.Max(height, (int)MathF.Ceiling(baseline - inkBottom * scale)) + growTop;
        baseline += growTop;

        var glyphs = placed.Select(p => new PositionedGlyph(p.Glyph, growLeft + options.Padding + p.PenUnits * scale)).ToList();

        return new LayoutResult(scale, baseline, Math.Max(1, width), Math.Max(1, height), glyphs);
    }

    private static int Overflow(float pixels) => pixels > 0 ? (int)MathF.Ceiling(pixels) : 0;
}
