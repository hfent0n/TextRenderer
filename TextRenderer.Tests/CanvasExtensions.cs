using System.Drawing;
using TextRenderer.Rendering;

namespace TextRenderer.Tests;

public static class CanvasExtensions
{
    public static Color GetPixel(this Canvas canvas, int x, int y)
    {
        var i = (y * canvas.Width + x) * 4;
        var p = canvas.Pixels;
        return Color.FromArgb(p[i + 3], p[i], p[i + 1], p[i + 2]);
    }
}
