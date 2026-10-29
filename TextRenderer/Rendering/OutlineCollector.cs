using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;

namespace TextRenderer.Rendering;

/// <summary>
/// Receives path commands from SixLabors and builds <see cref="Contour"/>s.
/// SixLabors reports Y-down (screen orientation); we flip to Y-up font units.
/// </summary>
internal sealed class OutlineCollector : IGlyphRenderer
{
    private readonly List<Contour> _contours = [];
    private List<PathSegment>? _segments;
    private Vector2 _start;

    public IReadOnlyList<Contour> Contours => _contours;

    public void MoveTo(Vector2 point)
    {
        CloseContour();
        _start = Flip(point);
        _segments = [];
    }

    public void LineTo(Vector2 point) => Add(new LineSegment(Flip(point)));

    public void QuadraticBezierTo(Vector2 control, Vector2 point) =>
        Add(new QuadSegment(Flip(control), Flip(point)));

    public void CubicBezierTo(Vector2 control1, Vector2 control2, Vector2 point) =>
        Add(new CubicSegment(Flip(control1), Flip(control2), Flip(point)));

    // Arcs only appear in SVG-in-OpenType colour glyphs, which we don't request.
    // Approximate with a straight line rather than failing the whole render.
    public void ArcTo(float radiusX, float radiusY, float rotation, bool largeArc, bool sweep, Vector2 point) =>
        LineTo(point);

    public void EndFigure() => CloseContour();

    public void EndGlyph() => CloseContour();

    private void Add(PathSegment segment)
    {
        if (_segments is null)
            throw new InvalidOperationException("Path segment received before MoveTo.");
        _segments.Add(segment);
    }

    private void CloseContour()
    {
        if (_segments is { Count: > 0 })
            _contours.Add(new Contour(_start, _segments));
        _segments = null;
    }

    private static Vector2 Flip(Vector2 p) => new(p.X, -p.Y);

    // Structural callbacks we don't need.
    public void BeginText(in FontRectangle bounds) { }
    public void EndText() { }
    public bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters) => true;
    public void BeginLayer(Paint? paint, FillRule fillRule) { }
    public void EndLayer() { }
    public void BeginGroup(CompositeMode mode) { }
    public void EndGroup() { }
    public void BeginFigure() { }
    public TextDecorations EnabledDecorations() => TextDecorations.None;
    public void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness, ReadOnlyMemory<float> dashPattern) { }
}
