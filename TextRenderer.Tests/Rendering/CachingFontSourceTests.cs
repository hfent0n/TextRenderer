using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class CachingFontSourceTests
{
    /// <summary>Counts calls to the inner source; slow enough that parallel callers really overlap.</summary>
    private sealed class CountingFontSource : IFontSource
    {
        private readonly FakeFontSource _inner = new();
        private int _calls;

        public int Calls => _calls;

        public FontMetrics Metrics => _inner.Metrics;

        public bool HasGlyph(int codepoint) => _inner.HasGlyph(codepoint);

        public Glyph GetGlyph(int codepoint)
        {
            Interlocked.Increment(ref _calls);
            Thread.Sleep(20);
            return _inner.GetGlyph(codepoint);
        }
    }

    [Fact]
    public void Repeat_calls_return_the_same_instance_and_hit_the_inner_source_once()
    {
        var inner = new CountingFontSource();
        var cache = new CachingFontSource(inner);

        var first = cache.GetGlyph('A');
        var second = cache.GetGlyph('A');

        Assert.Same(first, second);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public void Each_code_point_is_cached_separately()
    {
        var inner = new CountingFontSource();
        var cache = new CachingFontSource(inner);

        cache.GetGlyph('A');
        cache.GetGlyph('B');
        cache.GetGlyph('A');

        Assert.Equal(2, inner.Calls);
        Assert.NotSame(cache.GetGlyph('A'), cache.GetGlyph('B'));
    }

    [Fact]
    public void Parallel_callers_share_one_inner_call_per_code_point()
    {
        var inner = new CountingFontSource();
        var cache = new CachingFontSource(inner);
        using var start = new Barrier(32);

        // Dedicated threads: a Barrier on thread-pool threads stalls while the pool slowly adds threads.
        var glyphs = new Glyph[32];
        var threads = Enumerable.Range(0, 32).Select(i => new Thread(() =>
        {
            start.SignalAndWait();
            glyphs[i] = cache.GetGlyph("AB"[i % 2]);
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.Equal(2, inner.Calls);
        Assert.All(glyphs.Where((_, i) => i % 2 == 0), g => Assert.Same(glyphs[0], g));
        Assert.All(glyphs.Where((_, i) => i % 2 == 1), g => Assert.Same(glyphs[1], g));
    }

    [Fact]
    public void Metrics_and_has_glyph_pass_through()
    {
        var inner = new FakeFontSource();
        var cache = new CachingFontSource(inner);

        Assert.Same(inner.Metrics, cache.Metrics);
        Assert.True(cache.HasGlyph('A'));
        Assert.False(cache.HasGlyph('Z'));
    }

    [Fact]
    public void Cached_real_font_renders_identically_to_uncached()
    {
        var path = TestPaths.Font("DejaVuSerif.ttf");
        var options = new RenderOptions { FontSize = 32 };

        var uncached = new RenderPipeline(new SixLaborsFontSource(path)).Render("Cache me, cache me!", options);
        var cachedFont = new CachingFontSource(new SixLaborsFontSource(path));
        new RenderPipeline(cachedFont).Render("warm the cache", options);
        var cached = new RenderPipeline(cachedFont).Render("Cache me, cache me!", options);

        Assert.Equal(uncached.Pixels, cached.Pixels);
    }
}
