using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

/// <summary>Regression tests against approved images in <c>TextRenderer.Tests/golden/</c>.</summary>
public class GoldenImageTests
{
    public static TheoryData<string, int, string, string> Cases()
    {
        (string Slug, string Text)[] texts =
        [
            ("hello", "Hello, World!"),
            ("descenders", "Hgjpqy"),
            ("holes", "Oo@%&"),
            ("thin", "iiilll"),
        ];

        var data = new TheoryData<string, int, string, string>();
        foreach (var font in new[] { "DejaVuSans.ttf", "DejaVuSerif.ttf" })
        foreach (var size in new[] { 12, 48 })
        foreach (var (slug, text) in texts)
            data.Add(font, size, slug, text);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_golden_image(string fontFile, int size, string slug, string text)
    {
        var pipeline = new RenderPipeline(new SixLaborsFontSource(TestPaths.Font(fontFile)));

        var canvas = pipeline.Render(text, new RenderOptions { FontSize = size });

        GoldenImage.AssertMatches($"{Path.GetFileNameWithoutExtension(fontFile)}-{size}px-{slug}", canvas);
    }
}
