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

    [Fact]
    public void Pixel_is_filled_only_when_its_centre_is_inside()
    {
        // Edges at 2.4 and 5.6: centres 2.5..5.5 are inside, so pixels 2..5 in each direction.
        // Edges at 2.6 and 5.4 exclude centres 2.5 and 5.5, leaving pixels 3..4.
        Assert.Equal(Pixels(2, 2, 6, 6), Filled(Rasterizer.Rasterize(Rect(2.4f, 2.4f, 5.6f, 5.6f), 10, 10)));
        Assert.Equal(Pixels(3, 3, 5, 5), Filled(Rasterizer.Rasterize(Rect(2.6f, 2.6f, 5.4f, 5.4f), 10, 10)));
    }

    [Fact]
    public void Triangle_fills_pixels_under_the_diagonal()
    {
        // Right triangle (0,0) (4,0) (0,4): pixel centre (x+.5, y+.5) is inside when x + y + 1 < 4.
        Edge[] triangle = [new(0, 0, 4, 0), new(4, 0, 0, 4), new(0, 4, 0, 0)];

        var filled = Filled(Rasterizer.Rasterize(triangle, 10, 10));

        var expected = Pixels(0, 0, 4, 4).Where(p => p.Item1 + p.Item2 + 1 < 4).ToHashSet();
        Assert.Equal(expected, filled);
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
