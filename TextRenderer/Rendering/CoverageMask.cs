namespace TextRenderer.Rendering;

/// <summary>
/// Per-pixel coverage (0..1) for a rectangle of the canvas, row-major.
/// <see cref="OffsetX"/>/<see cref="OffsetY"/> place the rectangle in canvas pixel coordinates.
/// </summary>
public sealed record CoverageMask(int OffsetX, int OffsetY, int Width, int Height, float[] Coverage)
{
    public static readonly CoverageMask Empty = new(0, 0, 0, 0, []);

    /// <summary>Coverage at canvas pixel (<paramref name="x"/>, <paramref name="y"/>); 0 outside the mask.</summary>
    public float this[int x, int y]
    {
        get
        {
            int mx = x - OffsetX, my = y - OffsetY;
            return (uint)mx < Width && (uint)my < Height ? Coverage[my * Width + mx] : 0;
        }
    }
}
