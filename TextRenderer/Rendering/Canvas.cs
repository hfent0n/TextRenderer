using System.Drawing;

namespace TextRenderer.Rendering;

/// <summary>
/// An opaque RGBA pixel buffer, row-major from the top-left. Alpha is always 255;
/// the alpha of colours passed in is ignored.
/// </summary>
public sealed class Canvas
{
    private const int BytesPerPixel = 4;

    public Canvas(int width, int height, Color background)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        Pixels = new byte[width * height * BytesPerPixel];
        Clear(background);
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>RGBA bytes, <c>Width * Height * 4</c> long.</summary>
    public byte[] Pixels { get; }

    public void Clear(Color colour)
    {
        for (var i = 0; i < Pixels.Length; i += BytesPerPixel)
        {
            Pixels[i] = colour.R;
            Pixels[i + 1] = colour.G;
            Pixels[i + 2] = colour.B;
            Pixels[i + 3] = 255;
        }
    }

    /// <summary>
    /// Blends <paramref name="colour"/> over the existing pixel: <c>dst = dst·(1−a) + colour·a</c>,
    /// where <c>a</c> is <paramref name="coverage"/> clamped to [0, 1]. Pixels outside the canvas are ignored.
    /// </summary>
    public void Blend(int x, int y, float coverage, Color colour)
    {
        if ((uint)x >= Width || (uint)y >= Height || !(coverage > 0))
            return;

        var a = Math.Min(coverage, 1f);
        var i = (y * Width + x) * BytesPerPixel;
        Pixels[i] = Mix(Pixels[i], colour.R, a);
        Pixels[i + 1] = Mix(Pixels[i + 1], colour.G, a);
        Pixels[i + 2] = Mix(Pixels[i + 2], colour.B, a);
    }

    public Color GetPixel(int x, int y)
    {
        var i = (y * Width + x) * BytesPerPixel;
        return Color.FromArgb(Pixels[i + 3], Pixels[i], Pixels[i + 1], Pixels[i + 2]);
    }

    private static byte Mix(byte dst, byte src, float a) =>
        (byte)MathF.Round(dst + (src - dst) * a);
}
