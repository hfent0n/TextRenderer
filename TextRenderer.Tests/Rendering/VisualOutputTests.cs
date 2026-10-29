using System.Drawing;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

/// <summary>Writes images to <c>out/</c> for checking by eye. They also fail if writing throws.</summary>
public class VisualOutputTests
{
    [Fact]
    public void Writes_filled_rectangle_ppm()
    {
        var canvas = new Canvas(120, 80, Color.White);

        // Solid blue rectangle.
        for (var y = 20; y < 60; y++)
        for (var x = 20; x < 70; x++)
            canvas.Blend(x, y, 1, Color.RoyalBlue);

        // A vertical coverage ramp to the right, to see blending: transparent at the top, solid at the bottom.
        for (var y = 10; y < 70; y++)
        for (var x = 80; x < 110; x++)
            canvas.Blend(x, y, (y - 10) / 59f, Color.Crimson);

        var path = TestPaths.Output("test.ppm");
        File.WriteAllBytes(path, new PpmEncoder().Encode(canvas));

        Assert.True(new FileInfo(path).Length > 0);
    }
}
