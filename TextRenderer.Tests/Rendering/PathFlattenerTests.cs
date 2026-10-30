using System.Numerics;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class PathFlattenerTests
{
    private static Glyph GlyphOf(params Contour[] contours) => new(1, 100, contours);

    private static Contour Contour(Vector2 start, params PathSegment[] segments) => new(start, segments);

    private static Vector2 V(float x, float y) => new(x, y);

    /// <summary>Unit transform: scale 1, origin 0, baseline 0. Pixel y is just −font y.</summary>
    private static List<Edge> FlattenUnit(Glyph glyph) => PathFlattener.Flatten(glyph, 1, 0, 0);

    [Fact]
    public void Scales_offsets_and_flips_y()
    {
        var glyph = GlyphOf(Contour(V(4, 8), new LineSegment(V(4, 0))));

        var edges = PathFlattener.Flatten(glyph, scale: 0.5f, originX: 3, baselineY: 10);

        // x: 3 + 4·0.5 = 5.  y: 10 − 8·0.5 = 6 (above the baseline), 10 − 0 = 10 (on it).
        Assert.Equal(new Edge(5, 6, 5, 10), edges[0]);
    }

    [Fact]
    public void Line_contour_gives_one_edge_per_segment_plus_the_implicit_close()
    {
        var square = GlyphOf(Contour(V(0, 0), new LineSegment(V(10, 0)), new LineSegment(V(10, 10)), new LineSegment(V(0, 10))));

        var edges = FlattenUnit(square);

        Assert.Equal(4, edges.Count);
        Assert.Equal(new Edge(0, -10, 0, 0), edges[^1]);  // closing edge back to the start
    }

    [Fact]
    public void Explicitly_closed_contour_does_not_get_a_zero_length_closing_edge()
    {
        var square = GlyphOf(Contour(V(0, 0),
            new LineSegment(V(10, 0)), new LineSegment(V(10, 10)), new LineSegment(V(0, 10)), new LineSegment(V(0, 0))));

        Assert.Equal(4, FlattenUnit(square).Count);
    }

    [Fact]
    public void Edges_form_a_connected_closed_loop()
    {
        var glyph = GlyphOf(Contour(V(0, 0),
            new QuadSegment(V(50, 100), V(100, 0)),
            new CubicSegment(V(80, -50), V(20, -50), V(0, 0))));

        var edges = FlattenUnit(glyph);

        for (var i = 0; i < edges.Count; i++)
        {
            var next = edges[(i + 1) % edges.Count];
            Assert.Equal((edges[i].X1, edges[i].Y1), (next.X0, next.Y0));
        }
    }

    [Fact]
    public void Each_contour_is_closed_separately()
    {
        var outer = Contour(V(0, 0), new LineSegment(V(10, 0)), new LineSegment(V(10, 10)), new LineSegment(V(0, 10)));
        var inner = Contour(V(3, 3), new LineSegment(V(3, 7)), new LineSegment(V(7, 7)), new LineSegment(V(7, 3)));

        var edges = FlattenUnit(GlyphOf(outer, inner));

        Assert.Equal(8, edges.Count);
        Assert.Equal(new Edge(0, -10, 0, 0), edges[3]);  // outer closes to (0,0), not to the inner start
        Assert.Equal(new Edge(7, -3, 3, -3), edges[7]);
    }

    [Fact]
    public void Quad_preserves_endpoints_and_puts_every_vertex_on_the_curve()
    {
        // B(t) = (100t, 200t(1−t)), so every point satisfies y = 2x(1 − x/100).
        var glyph = GlyphOf(Contour(V(0, 0), new QuadSegment(V(50, 100), V(100, 0))));

        var curveEdges = FlattenUnit(glyph).SkipLast(1).ToList();  // drop the closing line

        Assert.True(curveEdges.Count > 1);
        Assert.Equal((0f, 0f), (curveEdges[0].X0, curveEdges[0].Y0));
        Assert.Equal((100f, 0f), (curveEdges[^1].X1, curveEdges[^1].Y1));
        foreach (var e in curveEdges)
        {
            var fontY = -e.Y1;
            Assert.Equal(2 * e.X1 * (1 - e.X1 / 100), fontY, 0.01f);
        }
    }

    [Fact]
    public void Cubic_preserves_endpoints_and_puts_every_vertex_on_the_curve()
    {
        // B(t) = (100t²(3 − 2t), 300t(1 − t)), sampled at t = i/n.
        var glyph = GlyphOf(Contour(V(0, 0), new CubicSegment(V(0, 100), V(100, 100), V(100, 0))));

        var curveEdges = FlattenUnit(glyph).SkipLast(1).ToList();
        var n = curveEdges.Count;

        Assert.True(n > 1);
        Assert.Equal((0f, 0f), (curveEdges[0].X0, curveEdges[0].Y0));
        Assert.Equal((100f, 0f), (curveEdges[^1].X1, curveEdges[^1].Y1));
        for (var i = 1; i <= n; i++)
        {
            var t = (float)i / n;
            Assert.Equal(100 * t * t * (3 - 2 * t), curveEdges[i - 1].X1, 0.01f);
            Assert.Equal(300 * t * (1 - t), -curveEdges[i - 1].Y1, 0.01f);
        }
    }

    [Fact]
    public void Bigger_curves_get_more_segments()
    {
        var glyph = GlyphOf(Contour(V(0, 0), new QuadSegment(V(50, 100), V(100, 0))));

        var small = PathFlattener.Flatten(glyph, 0.05f, 0, 0).Count;  // ~11px control polygon
        var large = PathFlattener.Flatten(glyph, 2f, 0, 0).Count;     // ~450px

        Assert.True(large > small, $"small {small}, large {large}");
    }

    [Fact]
    public void Tiny_curve_becomes_a_single_line()
    {
        var glyph = GlyphOf(Contour(V(0, 0), new QuadSegment(V(50, 100), V(100, 0))));

        var edges = PathFlattener.Flatten(glyph, 0.0005f, 0, 0);  // ~0.1px

        Assert.Equal(2, edges.Count);  // one for the curve, one closing
    }

    [Fact]
    public void Segment_count_is_capped()
    {
        var glyph = GlyphOf(Contour(V(0, 0), new QuadSegment(V(50, 100), V(100, 0))));

        var edges = PathFlattener.Flatten(glyph, 1000f, 0, 0);

        Assert.Equal(PathFlattener.MaxSegmentsPerCurve + 1, edges.Count);
    }

    [Fact]
    public void Glyph_without_contours_gives_no_edges()
    {
        Assert.Empty(FlattenUnit(GlyphOf()));
    }

    [Fact]
    public void Edge_winding_follows_screen_direction()
    {
        Assert.Equal(1, new Edge(0, 0, 0, 5).Winding);   // downwards
        Assert.Equal(-1, new Edge(0, 5, 0, 0).Winding);  // upwards
        Assert.Equal(0, new Edge(0, 3, 9, 3).Winding);   // horizontal
    }
}
