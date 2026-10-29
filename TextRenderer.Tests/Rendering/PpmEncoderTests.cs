using System.Drawing;
using System.Text;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class PpmEncoderTests
{
    private const string Header = "P6\n3 2\n255\n";

    [Fact]
    public void Writes_p6_header_then_three_bytes_per_pixel()
    {
        var bytes = new PpmEncoder().Encode(new Canvas(3, 2, Color.White));

        Assert.Equal(Header, Encoding.ASCII.GetString(bytes, 0, Header.Length));
        Assert.Equal(Header.Length + 3 * 2 * 3, bytes.Length);
    }

    [Fact]
    public void Writes_pixels_row_major_as_rgb_dropping_alpha()
    {
        var canvas = new Canvas(3, 2, Color.Black);
        canvas.Blend(1, 0, 1, Color.FromArgb(10, 20, 30));
        canvas.Blend(0, 1, 1, Color.FromArgb(40, 50, 60));

        var pixels = new PpmEncoder().Encode(canvas)[Header.Length..];

        Assert.Equal(
            new byte[]
            {
                0, 0, 0,    10, 20, 30, 0, 0, 0,
                40, 50, 60, 0, 0, 0,    0, 0, 0,
            },
            pixels);
    }

    [Fact]
    public void Reports_ppm_content_type()
    {
        Assert.Equal("image/x-portable-pixmap", new PpmEncoder().ContentType);
    }
}
