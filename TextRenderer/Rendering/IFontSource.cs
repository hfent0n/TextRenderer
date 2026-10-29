namespace TextRenderer.Rendering;

public interface IFontSource
{
    FontMetrics Metrics { get; }

    /// <summary>
    /// Returns the glyph for a Unicode code point. Code points the font doesn't map
    /// return the font's <c>.notdef</c> glyph (id 0) rather than throwing.
    /// </summary>
    Glyph GetGlyph(int codepoint);
}
