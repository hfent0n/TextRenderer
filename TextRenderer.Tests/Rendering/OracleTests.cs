using SkiaSharp;
using TextRenderer.Rendering;
using Xunit.Abstractions;

namespace TextRenderer.Tests.Rendering;

/// <summary>
/// Compares our renders against SkiaSharp, which has its own font parser and rasterizer.
/// Skia fills its own glyph outlines into an Alpha8 surface, so we compare raw coverage
/// (no text gamma, hinting or contrast tweaks). Our renders are black on white: coverage = (255 − R) / 255.
/// </summary>
public class OracleTests(ITestOutputHelper output)
{
    private const float MaxMeanDiff = 0.03f;

    /// <summary>
    /// IoU of "inked" pixels (coverage ≥ 0.5) is only meaningful once strokes are several pixels wide.
    /// At small sizes many edge pixels sit near 0.5 and flip between renderers: Skia computes exact areas,
    /// we sample 4×4, and even 16×16 sampling can't reach 0.95 at 12px. Mean difference is checked at every size.
    /// </summary>
    private static float? MinIoU(int size) => size switch
    {
        >= 64 => 0.95f,
        >= 24 => 0.90f,
        _ => null,
    };

    private static readonly string[] FontFiles = ["DejaVuSans.ttf", "DejaVuSansMono.ttf", "DejaVuSerif.ttf"];

    private static readonly string[] Texts =
    [
        "Hello, World!",
        "The quick brown fox jumps over the lazy dog.",
        "0123456789 +-×÷=<> 3.14159 $€£¥",
        "!\"#&'()*,./:;?@[\\]^_`{|}~",
        "Ångström café naïve façade Øresund",
    ];

    public static TheoryData<string, int, int> Cases()
    {
        var data = new TheoryData<string, int, int>();
        foreach (var font in FontFiles)
        foreach (var size in new[] { 12, 32, 96 })
        for (var text = 0; text < Texts.Length; text++)
            data.Add(font, size, text);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Per_glyph_matches_skia_at_our_pen_positions(string fontFile, int size, int textIndex)
    {
        var text = Texts[textIndex];
        var (ours, layout) = RenderOurs(fontFile, text, size);
        using var skFont = SkiaFont(fontFile, size);

        var positions = layout.Glyphs.Select(g => new SKPoint(g.PenX, layout.Baseline)).ToArray();
        var theirs = RenderSkia(skFont, text, positions, ours.Width, ours.Height);

        AssertSimilar(ours, theirs, size, $"per-glyph {fontFile} {size}px \"{text}\"");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Whole_string_matches_skia_layout(string fontFile, int size, int textIndex)
    {
        var text = Texts[textIndex];
        var (ours, layout) = RenderOurs(fontFile, text, size);
        using var skFont = SkiaFont(fontFile, size);

        // Skia's own glyph positions, starting at the same origin.
        var positions = skFont.GetGlyphPositions(text, new SKPoint(layout.Glyphs[0].PenX, layout.Baseline));
        var theirs = RenderSkia(skFont, text, positions, ours.Width, ours.Height);

        AssertSimilar(ours, theirs, size, $"whole-string {fontFile} {size}px \"{text}\"");
    }

    private static (Canvas Canvas, LayoutResult Layout) RenderOurs(string fontFile, string text, int size)
    {
        var font = new SixLaborsFontSource(TestPaths.Font(fontFile));
        var options = new RenderOptions { FontSize = size };
        return (new RenderPipeline(font).Render(text, options), TextLayout.Layout(text, font, options));
    }

    private static SKFont SkiaFont(string fontFile, int size) =>
        new(SKTypeface.FromFile(TestPaths.Font(fontFile)), size)
        {
            Hinting = SKFontHinting.None,
            Subpixel = true,
            LinearMetrics = true,
            Edging = SKFontEdging.Antialias,
        };

    /// <summary>Fills Skia's own outline for each glyph at the given baseline origins. Returns coverage 0..1.</summary>
    private static float[] RenderSkia(SKFont font, string text, SKPoint[] origins, int width, int height)
    {
        var glyphs = font.GetGlyphs(text);
        Assert.Equal(glyphs.Length, origins.Length);

        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black, Style = SKPaintStyle.Fill })
        {
            canvas.Clear(SKColors.Transparent);
            for (var i = 0; i < glyphs.Length; i++)
            {
                using var path = font.GetGlyphPath(glyphs[i]);
                if (path is null || path.IsEmpty)
                    continue;
                canvas.Save();
                canvas.Translate(origins[i].X, origins[i].Y);
                canvas.DrawPath(path, paint);
                canvas.Restore();
            }
        }

        var bytes = bitmap.Bytes;
        var coverage = new float[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            coverage[y * width + x] = bytes[y * bitmap.RowBytes + x] / 255f;
        return coverage;
    }

    private void AssertSimilar(Canvas ours, float[] theirs, int size, string label)
    {
        int width = ours.Width, height = ours.Height;
        int intersection = 0, union = 0;
        int left = width, top = height, right = -1, bottom = -1;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var a = (255 - ours.GetPixel(x, y).R) / 255f;
            var b = theirs[y * width + x];

            bool inkA = a >= 0.5f, inkB = b >= 0.5f;
            if (inkA && inkB) intersection++;
            if (inkA || inkB) union++;

            if (a > 0 || b > 0)
            {
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        }

        Assert.True(right >= 0, $"{label}: neither renderer drew anything");

        var diffSum = 0f;
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
            diffSum += MathF.Abs((255 - ours.GetPixel(x, y).R) / 255f - theirs[y * width + x]);

        var iou = union == 0 ? 1f : (float)intersection / union;
        var meanDiff = diffSum / ((right - left + 1) * (bottom - top + 1));

        var minIoU = MinIoU(size);
        output.WriteLine($"{label}: IoU {iou:F4}, mean diff {meanDiff:F4}");
        Assert.True((minIoU is null || iou >= minIoU) && meanDiff <= MaxMeanDiff,
            $"{label}: IoU {iou:F4} (need ≥ {minIoU?.ToString() ?? "n/a"}), mean diff {meanDiff:F4} (need ≤ {MaxMeanDiff})");
    }
}
