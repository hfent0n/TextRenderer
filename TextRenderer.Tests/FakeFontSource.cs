using System.Numerics;
using TextRenderer.Rendering;

namespace TextRenderer.Tests;

/// <summary>
/// A font with round numbers so layout and pixel results are exact.
/// 64 units per em: at FontSize 16 the scale is exactly 0.25 px/unit.
/// Ascender 48 (12px), descender −16 (4px).
/// Every non-space glyph is a solid box from x 4..(advance − 4), y 0..32, i.e. 1px in from each side and 8px tall at 16px.
/// </summary>
public sealed class FakeFontSource : IFontSource
{
    public static readonly Dictionary<int, int> Advances = new()
    {
        ['A'] = 32,  // 8px at size 16
        ['B'] = 48,  // 12px
        ['C'] = 33,  // 8.25px (fractional)
        [' '] = 16,  // 4px
    };

    public const int NotDefAdvance = 40;  // 10px

    public List<int> Requested { get; } = [];

    public FontMetrics Metrics { get; } = new(64, 48, -16, 0);

    public Glyph GetGlyph(int codepoint)
    {
        Requested.Add(codepoint);
        var known = Advances.TryGetValue(codepoint, out var advance);
        if (!known)
            advance = NotDefAdvance;

        if (codepoint == ' ')
            return new Glyph(1, advance, 0, []);

        var box = new Contour(new Vector2(4, 0),
        [
            new LineSegment(new Vector2(advance - 4, 0)),
            new LineSegment(new Vector2(advance - 4, 32)),
            new LineSegment(new Vector2(4, 32)),
        ]);
        return new Glyph(known ? codepoint : 0, advance, 4, [box]);
    }
}
