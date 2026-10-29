using System.Collections.Concurrent;

namespace TextRenderer.Rendering;

/// <summary>
/// Caches glyphs from another font source. Glyphs are in font units (size-independent),
/// so one cache per font serves every font size. Safe to share across threads.
/// </summary>
public sealed class CachingFontSource(IFontSource inner) : IFontSource
{
    // Lazy guarantees the inner source is asked once per code point, even when threads race:
    // ConcurrentDictionary.GetOrAdd alone may run the factory more than once.
    private readonly ConcurrentDictionary<int, Lazy<Glyph>> _glyphs = new();

    public FontMetrics Metrics => inner.Metrics;

    public bool HasGlyph(int codepoint) => inner.HasGlyph(codepoint);

    public Glyph GetGlyph(int codepoint) =>
        _glyphs.GetOrAdd(codepoint, cp => new Lazy<Glyph>(() => inner.GetGlyph(cp))).Value;
}
