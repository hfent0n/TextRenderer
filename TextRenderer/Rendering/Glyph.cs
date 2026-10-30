using System.Numerics;

namespace TextRenderer.Rendering;

/// <summary>
/// A glyph's outline and horizontal metrics in font units (Y-up, baseline at 0).
/// Independent of font size, so it can be cached per (font, glyph).
/// </summary>
public sealed record Glyph(int GlyphId, int AdvanceWidth, int LeftSideBearing, IReadOnlyList<Contour> Contours)
{
    /// <summary>Outline bounds in font units, or null if the glyph has no outline (e.g. a space).</summary>
    public GlyphBounds? Bounds { get; } = GlyphBounds.Of(Contours);
}

/// <summary>Box around a glyph's outline. Includes curve control points, so it can be slightly larger than the ink.</summary>
public sealed record GlyphBounds(float MinX, float MinY, float MaxX, float MaxY)
{
    public static GlyphBounds? Of(IReadOnlyList<Contour> contours)
    {
        var points = contours.SelectMany(c => c.Segments.SelectMany(Points).Prepend(c.Start)).ToList();
        return points.Count == 0
            ? null
            : new GlyphBounds(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }

    private static IEnumerable<Vector2> Points(PathSegment segment) => segment switch
    {
        QuadSegment q => [q.Control, q.End],
        CubicSegment c => [c.Control1, c.Control2, c.End],
        _ => [segment.End],
    };
}

/// <summary>A closed path: starts at <see cref="Start"/>, and the last segment implicitly joins back to it.</summary>
public sealed record Contour(Vector2 Start, IReadOnlyList<PathSegment> Segments);

public abstract record PathSegment(Vector2 End);

public sealed record LineSegment(Vector2 End) : PathSegment(End);

public sealed record QuadSegment(Vector2 Control, Vector2 End) : PathSegment(End);

public sealed record CubicSegment(Vector2 Control1, Vector2 Control2, Vector2 End) : PathSegment(End);
