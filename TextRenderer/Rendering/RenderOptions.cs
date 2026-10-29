using System.Drawing;

namespace TextRenderer.Rendering;

/// <summary>How to render text. Invalid values throw when the options are created (including via <c>with</c>).</summary>
public sealed record RenderOptions
{
    private readonly float _fontSize = 48;
    private readonly int _padding = 8;

    /// <summary>Font size in pixels per em.</summary>
    public float FontSize
    {
        get => _fontSize;
        init
        {
            if (!(value > 0) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(FontSize), value, "Font size must be a positive, finite number.");
            _fontSize = value;
        }
    }

    /// <summary>Blank space in pixels around the text on every side.</summary>
    public int Padding
    {
        get => _padding;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(Padding));
            _padding = value;
        }
    }

    public Color Foreground { get; init; } = Color.Black;

    public Color Background { get; init; } = Color.White;
}
