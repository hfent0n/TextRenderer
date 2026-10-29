using System.Drawing;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class RenderOptionsTests
{
    [Fact]
    public void Has_sensible_defaults()
    {
        var options = new RenderOptions();

        Assert.Equal(48f, options.FontSize);
        Assert.Equal(8, options.Padding);
        Assert.Equal(Color.Black, options.Foreground);
        Assert.Equal(Color.White, options.Background);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-12f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Rejects_invalid_font_size(float size)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new RenderOptions { FontSize = size });
        Assert.Equal(nameof(RenderOptions.FontSize), ex.ParamName);
    }

    [Fact]
    public void Rejects_negative_padding()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new RenderOptions { Padding = -1 });
        Assert.Equal(nameof(RenderOptions.Padding), ex.ParamName);
    }

    [Fact]
    public void Validation_also_applies_to_with_expressions()
    {
        var options = new RenderOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options with { FontSize = 0 });
    }

    [Fact]
    public void Allows_zero_padding_and_small_fractional_sizes()
    {
        var options = new RenderOptions { Padding = 0, FontSize = 0.5f };

        Assert.Equal((0, 0.5f), (options.Padding, options.FontSize));
    }
}
