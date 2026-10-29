using System.Drawing;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class RasterizerTests
{
    /// <summary>Clockwise (on screen) rectangle edges.</summary>
    private static Edge[] Rect(float x0, float y0, float x1, float y1) =>
    [
        new(x0, y0, x1, y0),
        new(x1, y0, x1, y1),
        new(x1, y1, x0, y1),
        new(x0, y1, x0, y0),
    ];

    /// <summary>Same rectangle, traced the opposite way.</summary>
    private static Edge[] ReversedRect(float x0, float y0, float x1, float y1) =>
        Rect(x0, y0, x1, y1).Reverse().Select(e => new Edge(e.X1, e.Y1, e.X0, e.Y0)).ToArray();

    /// <summary>Filled pixels as (x, y) in canvas coordinates.</summary>
    private static HashSet<(int, int)> Filled(CoverageMask mask)
    {
        var set = new HashSet<(int, int)>();
        for (var y = 0; y < mask.Height; y++)
        for (var x = 0; x < mask.Width; x++)
            if (mask.Coverage[y * mask.Width + x] > 0)
                set.Add((mask.OffsetX + x, mask.OffsetY + y));
        return set;
    }

    private static HashSet<(int, int)> Pixels(int x0, int y0, int x1, int y1) =>
        Enumerable.Range(x0, x1 - x0).SelectMany(x => Enumerable.Range(y0, y1 - y0).Select(y => (x, y))).ToHashSet();

    [Fact]
    public void Axis_aligned_square_fills_exactly_its_pixels()
    {
        var mask = Rasterizer.Rasterize(Rect(2, 2, 6, 6), 10, 10);

        Assert.Equal((2, 2, 4, 4), (mask.OffsetX, mask.OffsetY, mask.Width, mask.Height));
        Assert.Equal(Pixels(2, 2, 6, 6), Filled(mask));
        Assert.All(mask.Coverage, c => Assert.Equal(1f, c));
    }

    [Fact]
    public void Direction_of_travel_does_not_matter_for_a_single_shape()
    {
        var mask = Rasterizer.Rasterize(ReversedRect(2, 2, 6, 6), 10, 10);

        Assert.Equal(Pixels(2, 2, 6, 6), Filled(mask));
    }

    [Fact]
    public void Reversed_inner_square_leaves_a_hole()
    {
        var edges = Rect(1, 1, 7, 7).Concat(ReversedRect(3, 3, 5, 5)).ToArray();

        var filled = Filled(Rasterizer.Rasterize(edges, 10, 10));

        Assert.Equal(Pixels(1, 1, 7, 7).Except(Pixels(3, 3, 5, 5)).ToHashSet(), filled);
    }

    [Fact]
    public void Overlapping_squares_in_the_same_direction_fill_their_union()
    {
        // Non-zero winding: the overlap has winding 2, which is still "inside". Even-odd would leave a hole.
        var edges = Rect(1, 1, 5, 5).Concat(Rect(3, 3, 7, 7)).ToArray();

        var filled = Filled(Rasterizer.Rasterize(edges, 10, 10));

        Assert.Equal(Pixels(1, 1, 5, 5).Union(Pixels(3, 3, 7, 7)).ToHashSet(), filled);
    }

    [Theory]
    [InlineData(2.0f, 1.0f)]    // all 4 sample columns (2.125, 2.375, 2.625, 2.875) inside
    [InlineData(2.2f, 0.75f)]   // 2.125 is now outside
    [InlineData(2.4f, 0.5f)]
    [InlineData(2.5f, 0.5f)]    // edge through the pixel's middle
    [InlineData(2.7f, 0.25f)]
    [InlineData(2.9f, 0.0f)]    // past the last sample
    public void Vertical_edge_inside_a_pixel_gives_partial_coverage(float edgeX, float expected)
    {
        var mask = Rasterizer.Rasterize(Rect(edgeX, 0, 6, 4), 10, 10);

        for (var y = 0; y < 4; y++)
        {
            Assert.Equal(expected, mask[2, y]);
            Assert.Equal(1f, mask[3, y]);
        }
    }

    [Fact]
    public void Corner_pixel_coverage_is_the_product_of_both_edges()
    {
        var mask = Rasterizer.Rasterize(Rect(2.5f, 2.5f, 6, 6), 10, 10);

        Assert.Equal(0.25f, mask[2, 2]);  // half in x × half in y
        Assert.Equal(0.5f, mask[2, 4]);   // half in x only
        Assert.Equal(0.5f, mask[4, 2]);   // half in y only
        Assert.Equal(1f, mask[4, 4]);
    }

    [Fact]
    public void Sliver_thinner_than_a_pixel_still_shows()
    {
        var mask = Rasterizer.Rasterize(Rect(2, 0, 2.25f, 4), 10, 10);

        Assert.Equal(Pixels(2, 0, 3, 4), Filled(mask));
        Assert.All(mask.Coverage, c => Assert.Equal(0.25f, c));
    }

    [Fact]
    public void Triangle_coverage_matches_its_area()
    {
        // Right triangle (0,0) (4,0) (0,4): area 8 px². The diagonal cuts through pixels, giving partial coverage.
        Edge[] triangle = [new(0, 0, 4, 0), new(4, 0, 0, 4), new(0, 4, 0, 0)];

        var mask = Rasterizer.Rasterize(triangle, 10, 10);

        // True area is 8, but the diagonal x + y = 4 passes exactly through 16 of the 256 samples
        // (sub-sample indices i + j = 15), which the half-open rule counts as outside: 120 / 16 = 7.5.
        // A diagonal aligned with the sample grid is the worst case for 4×4 supersampling.
        Assert.Equal(7.5f, mask.Coverage.Sum(), 0.001f);
        Assert.Equal(1f, mask[0, 0]);                  // well inside
        Assert.InRange(mask[1, 2], 0.01f, 0.99f);      // on the diagonal
        Assert.Equal(0f, mask[3, 3]);                  // outside
    }

    [Fact]
    public void Overlapping_shapes_do_not_exceed_full_coverage()
    {
        // Winding 2 in the overlap still counts each sample once.
        var edges = Rect(1, 1, 5.5f, 5.5f).Concat(Rect(3.5f, 3.5f, 7, 7)).ToArray();

        var mask = Rasterizer.Rasterize(edges, 10, 10);

        Assert.Equal(1f, mask[4, 4]);
        Assert.All(mask.Coverage, c => Assert.InRange(c, 0f, 1f));
    }

    [Fact]
    public void Real_glyph_has_full_and_partial_coverage_all_within_range()
    {
        var font = new SixLaborsFontSource(TestPaths.Font("DejaVuSans.ttf"));
        var glyph = font.GetGlyph('@');
        var scale = 48f / font.Metrics.UnitsPerEm;

        var mask = Rasterizer.Rasterize(PathFlattener.Flatten(glyph, scale, 0, 48), 100, 100);

        Assert.All(mask.Coverage, c => Assert.InRange(c, 0f, 1f));
        Assert.Contains(mask.Coverage, c => c == 1f);
        Assert.Contains(mask.Coverage, c => c is > 0f and < 1f);
    }

    [Fact]
    public void Shape_partly_off_canvas_is_clipped()
    {
        var mask = Rasterizer.Rasterize(Rect(-2, -2, 3, 3), 10, 10);

        Assert.Equal((0, 0), (mask.OffsetX, mask.OffsetY));
        Assert.Equal(Pixels(0, 0, 3, 3), Filled(mask));
    }

    [Fact]
    public void Shape_past_the_right_and_bottom_is_clipped()
    {
        var mask = Rasterizer.Rasterize(Rect(7, 7, 12, 12), 10, 10);

        Assert.Equal(Pixels(7, 7, 10, 10), Filled(mask));
    }

    [Fact]
    public void Shape_entirely_off_canvas_gives_an_empty_mask()
    {
        Assert.Same(CoverageMask.Empty, Rasterizer.Rasterize(Rect(20, 20, 25, 25), 10, 10));
        Assert.Same(CoverageMask.Empty, Rasterizer.Rasterize(Rect(-9, 2, -4, 6), 10, 10));
    }

    [Fact]
    public void No_edges_gives_an_empty_mask()
    {
        Assert.Same(CoverageMask.Empty, Rasterizer.Rasterize([], 10, 10));
    }

    [Fact]
    public void Mask_indexer_uses_canvas_coordinates_and_is_zero_outside()
    {
        var mask = Rasterizer.Rasterize(Rect(2, 2, 6, 6), 10, 10);

        Assert.Equal(1f, mask[2, 2]);
        Assert.Equal(1f, mask[5, 5]);
        Assert.Equal(0f, mask[1, 2]);
        Assert.Equal(0f, mask[6, 5]);
    }

    [Fact]
    public void Canvas_blends_a_mask_at_its_offset()
    {
        var canvas = new Canvas(10, 10, Color.White);

        canvas.Blend(Rasterizer.Rasterize(Rect(2, 2, 6, 6), 10, 10), Color.Black);

        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 10; x++)
        {
            var inside = x is >= 2 and < 6 && y is >= 2 and < 6;
            Assert.Equal(inside ? 0 : 255, canvas.GetPixel(x, y).R);
        }
    }
}
