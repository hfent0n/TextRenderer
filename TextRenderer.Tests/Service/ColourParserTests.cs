using System.Drawing;
using TextRenderer.Service;

namespace TextRenderer.Tests.Service;

public class ColourParserTests
{
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#1A2B3C", 0x1A, 0x2B, 0x3C)]
    [InlineData("#ff00aa", 255, 0, 170)]   // lower-case hex
    [InlineData("#Ff00aA", 255, 0, 170)]   // mixed case
    public void Parses_hex_colours(string value, int r, int g, int b)
    {
        Assert.True(ColourParser.TryParse(value, out var colour));
        Assert.Equal(Color.FromArgb(255, r, g, b).ToArgb(), colour.ToArgb());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("000000")]      // missing #
    [InlineData("#FFF")]        // short form not supported
    [InlineData("#FFFFFFFF")]   // alpha not supported
    [InlineData("#GGGGGG")]
    [InlineData("#12345 ")]
    [InlineData("# 12345")]
    [InlineData("#-12345")]
    [InlineData("red")]
    public void Rejects_anything_else(string? value)
    {
        Assert.False(ColourParser.TryParse(value, out _));
    }
}
