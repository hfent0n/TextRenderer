using TextRenderer.Rendering;

namespace TextRenderer.Tests;

/// <summary>A <see cref="FakeFontSource"/> whose every glyph lookup takes a fixed time and is counted.</summary>
public sealed class SlowFontSource(TimeSpan delay) : IFontSource
{
    private readonly FakeFontSource _inner = new();
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public FontMetrics Metrics => _inner.Metrics;

    public bool HasGlyph(int codepoint) => _inner.HasGlyph(codepoint);

    public Glyph GetGlyph(int codepoint)
    {
        Interlocked.Increment(ref _calls);
        Thread.Sleep(delay);
        lock (_inner)
            return _inner.GetGlyph(codepoint);
    }
}
