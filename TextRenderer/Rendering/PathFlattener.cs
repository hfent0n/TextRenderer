using System.Numerics;

namespace TextRenderer.Rendering;

/// <summary>
/// Converts a glyph outline (font units, Y-up) into straight edges in pixel space (Y-down).
/// This is the only place scaling and the Y-flip happen.
/// </summary>
public static class PathFlattener
{
    /// <summary>Target maximum distance in pixels between a curve and its straight-line approximation.</summary>
    public const float Tolerance = 0.2f;

    public const int MaxSegmentsPerCurve = 64;

    /// <param name="scale">Pixels per font unit (<c>fontSize / unitsPerEm</c>).</param>
    /// <param name="originX">Pixel x of the glyph's pen position.</param>
    /// <param name="baselineY">Pixel y of the baseline.</param>
    public static List<Edge> Flatten(Glyph glyph, float scale, float originX, float baselineY)
    {
        var edges = new List<Edge>();
        Vector2 ToPixels(Vector2 p) => new(originX + p.X * scale, baselineY - p.Y * scale);

        foreach (var contour in glyph.Contours)
        {
            var start = ToPixels(contour.Start);
            var current = start;

            foreach (var segment in contour.Segments)
            {
                var end = ToPixels(segment.End);
                switch (segment)
                {
                    case LineSegment:
                        AddLine(edges, current, end);
                        break;
                    case QuadSegment q:
                        AddQuad(edges, current, ToPixels(q.Control), end);
                        break;
                    case CubicSegment c:
                        AddCubic(edges, current, ToPixels(c.Control1), ToPixels(c.Control2), end);
                        break;
                }
                current = end;
            }

            AddLine(edges, current, start);  // contours are implicitly closed
        }

        return edges;
    }

    private static void AddLine(List<Edge> edges, Vector2 from, Vector2 to)
    {
        if (from != to)
            edges.Add(new Edge(from.X, from.Y, to.X, to.Y));
    }

    private static void AddQuad(List<Edge> edges, Vector2 p0, Vector2 c, Vector2 p1)
    {
        var n = SegmentCount((c - p0).Length() + (p1 - c).Length());
        var previous = p0;
        for (var i = 1; i <= n; i++)
        {
            var t = (float)i / n;
            var u = 1 - t;
            var point = i == n ? p1 : u * u * p0 + 2 * u * t * c + t * t * p1;
            AddLine(edges, previous, point);
            previous = point;
        }
    }

    private static void AddCubic(List<Edge> edges, Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p1)
    {
        var n = SegmentCount((c1 - p0).Length() + (c2 - c1).Length() + (p1 - c2).Length());
        var previous = p0;
        for (var i = 1; i <= n; i++)
        {
            var t = (float)i / n;
            var u = 1 - t;
            var point = i == n ? p1 : u * u * u * p0 + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * p1;
            AddLine(edges, previous, point);
            previous = point;
        }
    }

    /// <summary>Number of line segments for a curve whose control polygon is <paramref name="length"/> pixels long.</summary>
    internal static int SegmentCount(float length) =>
        Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(length / Tolerance)), 1, MaxSegmentsPerCurve);
}
