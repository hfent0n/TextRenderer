using System.Numerics;

namespace TextRenderer.Rendering;

/// <summary>
/// A glyph's outline and horizontal metrics in font units (Y-up, baseline at 0).
/// Independent of font size, so it can be cached per (font, glyph).
/// </summary>
public sealed record Glyph(int GlyphId, int AdvanceWidth, int LeftSideBearing, IReadOnlyList<Contour> Contours);

/// <summary>A closed path: starts at <see cref="Start"/>, and the last segment implicitly joins back to it.</summary>
public sealed record Contour(Vector2 Start, IReadOnlyList<PathSegment> Segments);

public abstract record PathSegment(Vector2 End);

public sealed record LineSegment(Vector2 End) : PathSegment(End);

public sealed record QuadSegment(Vector2 Control, Vector2 End) : PathSegment(End);

public sealed record CubicSegment(Vector2 Control1, Vector2 Control2, Vector2 End) : PathSegment(End);
