using System.Numerics;
using System.Text;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SLFont = SixLabors.Fonts.Font;
using SLGlyphRenderer = SixLabors.Fonts.Rendering.TextRenderer;

namespace TextRenderer.Rendering;

/// <summary>
/// The only class that touches SixLabors.Fonts. Used purely to parse the font:
/// metrics and glyph outlines. Layout and rasterization are ours.
/// </summary>
public sealed class SixLaborsFontSource : IFontSource
{
    private const ushort NotDefGlyphId = 0;

    private readonly SLFont _font;
    private readonly GlyphOptions _outlineOptions;

    public SixLaborsFontSource(string path) : this(new FontCollection().Add(path)) { }

    public SixLaborsFontSource(Stream stream) : this(new FontCollection().Add(stream)) { }

    private SixLaborsFontSource(FontFamily family)
    {
        var unitsPerEm = family.CreateFont(1).FontMetrics.UnitsPerEm;

        // Point size == units-per-em at 72 DPI makes 1 output unit == 1 font unit,
        // so outlines come back unscaled. Origin is the alphabetic baseline.
        _font = family.CreateFont(unitsPerEm);
        _outlineOptions = new GlyphOptions
        {
            Font = _font,
            Dpi = 72,
            HintingMode = HintingMode.None,
            TextBaseline = TextBaseline.Alphabetic,
            Origin = Vector2.Zero,
        };

        var h = _font.FontMetrics.HorizontalMetrics;
        Metrics = new FontMetrics(unitsPerEm, h.Ascender, h.Descender, h.LineGap);
    }

    public FontMetrics Metrics { get; }

    public Glyph GetGlyph(int codepoint)
    {
        var glyphId = Rune.IsValid(codepoint) && _font.TryGetGlyphId(new CodePoint(codepoint), out var id)
            ? id
            : NotDefGlyphId;

        _font.FontMetrics.TryGetGlyphMetrics(
            glyphId, TextAttributes.None, TextDecorations.None, LayoutMode.HorizontalTopBottom,
            ColorFontSupport.None, null, out var metrics);

        var collector = new OutlineCollector();
        SLGlyphRenderer.RenderTo(collector, glyphId, _outlineOptions);

        return new Glyph(glyphId, metrics?.AdvanceWidth ?? 0, metrics?.LeftSideBearing ?? 0, collector.Contours);
    }
}
