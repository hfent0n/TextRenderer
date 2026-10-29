namespace TextRenderer.Rendering;

/// <summary>A straight line segment in pixel space (Y-down), in the direction the contour travels.</summary>
public readonly record struct Edge(float X0, float Y0, float X1, float Y1)
{
    /// <summary>+1 if the edge goes down the screen, −1 if up, 0 if horizontal.</summary>
    public int Winding => Y1 > Y0 ? 1 : Y1 < Y0 ? -1 : 0;
}
