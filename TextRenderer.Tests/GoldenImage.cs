using SkiaSharp;
using TextRenderer.Rendering;

namespace TextRenderer.Tests;

/// <summary>
/// Compares a canvas with an approved PNG in <c>TextRenderer.Tests/golden/</c>.
/// Missing goldens are written and the test fails, so nothing is approved silently.
/// Set <c>UPDATE_GOLDEN=1</c> to regenerate on purpose.
/// </summary>
public static class GoldenImage
{
    /// <summary>A pixel "differs" if any channel is off by more than this.</summary>
    public const int ChannelTolerance = 2;

    /// <summary>Maximum fraction of pixels allowed to differ.</summary>
    public const double MaxDifferingFraction = 0.001;

    private static string Directory => Path.Combine(TestPaths.RepoRoot, "TextRenderer.Tests", "golden");

    public static void AssertMatches(string name, Canvas canvas)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var goldenPath = Path.Combine(Directory, $"{name}.png");
        var actualPath = Path.Combine(Directory, $"{name}.actual.png");
        var diffPath = Path.Combine(Directory, $"{name}.diff.png");
        File.Delete(actualPath);
        File.Delete(diffPath);

        var png = PngEncoder.Encode(canvas);

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            File.WriteAllBytes(goldenPath, png);
            return;
        }

        if (!File.Exists(goldenPath))
        {
            File.WriteAllBytes(goldenPath, png);
            Assert.Fail($"No golden image existed, so one was written to {goldenPath}. Review it by eye, commit it, and re-run.");
        }

        using var golden = SKBitmap.Decode(goldenPath);
        if (golden.Width != canvas.Width || golden.Height != canvas.Height)
        {
            File.WriteAllBytes(actualPath, png);
            Assert.Fail($"Size changed: golden {golden.Width}x{golden.Height}, actual {canvas.Width}x{canvas.Height}. See {actualPath}.");
        }

        var differing = 0;
        var maxDelta = 0;
        using var diff = new SKBitmap(canvas.Width, canvas.Height);
        for (var y = 0; y < canvas.Height; y++)
        for (var x = 0; x < canvas.Width; x++)
        {
            var expected = golden.GetPixel(x, y);
            var actual = canvas.GetPixel(x, y);
            var delta = Math.Max(Math.Abs(expected.Red - actual.R),
                Math.Max(Math.Abs(expected.Green - actual.G), Math.Abs(expected.Blue - actual.B)));
            maxDelta = Math.Max(maxDelta, delta);

            if (delta > ChannelTolerance)
            {
                differing++;
                diff.SetPixel(x, y, SKColors.Red);
            }
            else
            {
                var grey = (byte)(255 - (255 - expected.Red) / 4);  // faded golden for context
                diff.SetPixel(x, y, new SKColor(grey, grey, grey));
            }
        }

        var allowed = (int)(canvas.Width * canvas.Height * MaxDifferingFraction);
        if (differing > allowed)
        {
            File.WriteAllBytes(actualPath, png);
            using (var stream = File.OpenWrite(diffPath))
                diff.Encode(stream, SKEncodedImageFormat.Png, 100);
            Assert.Fail($"{differing} pixels differ by more than {ChannelTolerance} (allowed {allowed}, max delta {maxDelta}). " +
                        $"See {actualPath} and {diffPath}.");
        }
    }
}
