using System.Drawing;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

/// <summary>Writes images to <c>out/</c> for checking by eye. They also fail if writing throws.</summary>
public class VisualOutputTests
{
    [Fact]
    public void Writes_filled_rectangle_png()
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

        Write("test.png", canvas);
    }

    [Theory]
    [InlineData('H')]
    [InlineData('o')]
    public void Writes_single_glyph_png(char character)
    {
        var pipeline = new RenderPipeline(new SixLaborsFontSource(TestPaths.Font("DejaVuSans.ttf")));

        var canvas = pipeline.Render(character.ToString(), new RenderOptions { FontSize = 96 });

        Write($"glyph-{character}.png", canvas);
    }

    [Theory]
    [InlineData("DejaVuSans.ttf")]
    [InlineData("DejaVuSansMono.ttf")]
    [InlineData("DejaVuSerif.ttf")]
    public void Writes_hello_world_png(string fontFile)
    {
        var pipeline = new RenderPipeline(new SixLaborsFontSource(TestPaths.Font(fontFile)));

        var canvas = pipeline.Render("Hello, World!", new RenderOptions());

        Write($"hello-{Path.GetFileNameWithoutExtension(fontFile)}.png", canvas);
    }

    private static readonly string[] FontFiles = ["DejaVuSans.ttf", "DejaVuSansMono.ttf", "DejaVuSerif.ttf"];

    private static readonly (string Name, string Text)[] SampleTexts =
    [
        ("pangram", "The quick brown fox jumps over the lazy dog."),
        ("pangram-upper", "THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG!"),
        ("digits", "0123456789 +-×÷=<> 3.14159 1,234 50% $€£¥"),
        ("punctuation", "!\"#&'()*,./:;?@[\\]^_`{|}~ «quote» — dash…"),
        ("accents", "Ångström café naïve façade Øresund Ærø crème brûlée"),
    ];

    public static TheoryData<string, string, string> Samples()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var font in FontFiles)
        foreach (var (name, text) in SampleTexts)
            data.Add(font, name, text);
        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Writes_sample_text_png(string fontFile, string name, string text)
    {
        var font = new SixLaborsFontSource(TestPaths.Font(fontFile));

        // Every character in the samples exists in DejaVu, so none should fall back to .notdef.
        var missing = text.EnumerateRunes().Where(r => font.GetGlyph(r.Value).GlyphId == 0).ToList();
        Assert.Empty(missing);

        var canvas = new RenderPipeline(font).Render(text, new RenderOptions { FontSize = 32 });

        Write($"sample-{Path.GetFileNameWithoutExtension(fontFile)}-{name}.png", canvas);
    }

    [Theory]
    [InlineData("DejaVuSans.ttf")]
    [InlineData("DejaVuSansMono.ttf")]
    [InlineData("DejaVuSerif.ttf")]
    public void Writes_small_pangram_png(string fontFile)
    {
        // Small sizes stress anti-aliasing: stems are only 1–2px wide.
        var pipeline = new RenderPipeline(new SixLaborsFontSource(TestPaths.Font(fontFile)));

        var canvas = pipeline.Render(SampleTexts[0].Text, new RenderOptions { FontSize = 12, Padding = 4 });

        Write($"sample-{Path.GetFileNameWithoutExtension(fontFile)}-pangram-12px.png", canvas);
    }

    private static void Write(string fileName, Canvas canvas)
    {
        var path = TestPaths.Output(fileName);
        File.WriteAllBytes(path, new PngEncoder().Encode(canvas));
        Assert.True(new FileInfo(path).Length > 0);
    }
}
