using System.Drawing;

namespace TextRenderer.Rendering;

/// <summary>How to render text. Requests are validated before these are created.</summary>
public sealed record RenderOptions
{
    /// <summary>Font size in pixels per em.</summary>
    public float FontSize { get; init; } = 48;

    /// <summary>Blank space in pixels around the text on every side.</summary>
    public int Padding { get; init; } = 8;

    public Color Foreground { get; init; } = Color.Black;

    public Color Background { get; init; } = Color.White;
}
