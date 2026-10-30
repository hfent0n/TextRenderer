using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class CancellationTests
{
    /// <summary>Cancels the token once a given number of glyphs have been requested.</summary>
    private sealed class CancellingFontSource(CancellationTokenSource source, int cancelAfter) : IFontSource
    {
        private readonly FakeFontSource _inner = new();

        public int Calls { get; private set; }

        public FontMetrics Metrics => _inner.Metrics;

        public bool HasGlyph(int codepoint) => _inner.HasGlyph(codepoint);

        public Glyph GetGlyph(int codepoint)
        {
            if (++Calls == cancelAfter)
                source.Cancel();
            return _inner.GetGlyph(codepoint);
        }
    }

    private static readonly RenderOptions Options = new() { FontSize = 16 };

    [Fact]
    public void Layout_stops_at_the_next_glyph_once_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var font = new CancellingFontSource(cts, cancelAfter: 3);

        Assert.ThrowsAny<OperationCanceledException>(() => TextLayout.Layout("ABCABCABC", font, Options, cts.Token));

        Assert.Equal(3, font.Calls);  // not all 9
    }

    [Fact]
    public void Pipeline_throws_when_already_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var font = new FakeFontSource();

        Assert.ThrowsAny<OperationCanceledException>(() => new RenderPipeline(font).Render("ABC", Options, cts.Token));

        Assert.Empty(font.Requested);
    }

    [Fact]
    public void Pipeline_stops_when_cancelled_during_layout()
    {
        using var cts = new CancellationTokenSource();
        var font = new CancellingFontSource(cts, cancelAfter: 2);

        Assert.ThrowsAny<OperationCanceledException>(() => new RenderPipeline(font).Render("ABCABC", Options, cts.Token));

        Assert.Equal(2, font.Calls);
    }

    [Fact]
    public void An_uncancelled_token_changes_nothing()
    {
        using var cts = new CancellationTokenSource();
        var pipeline = new RenderPipeline(new FakeFontSource());

        Assert.Equal(pipeline.Render("ABC", Options).Pixels, pipeline.Render("ABC", Options, cts.Token).Pixels);
    }
}
