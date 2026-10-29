namespace TextRenderer.Rendering;

/// <summary>Service limits, bound from the <c>Render</c> section of configuration.</summary>
public sealed class RenderLimits
{
    public const string SectionName = "Render";

    /// <summary>Maximum text length in Unicode code points.</summary>
    public int MaxTextLength { get; set; } = 500;

    public float MinFontSize { get; set; } = 1;

    public float MaxFontSize { get; set; } = 512;

    public int MaxPadding { get; set; } = 256;

    /// <summary>Maximum canvas width × height. The limit that stops a single request exhausting memory.</summary>
    public long MaxCanvasPixels { get; set; } = 16_000_000;

    public long MaxRequestBodyBytes { get; set; } = 16 * 1024;

    public TimeSpan RenderTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Renders allowed to run at once. Rendering is CPU-bound, so defaults to the core count.</summary>
    public int MaxConcurrentRenders { get; set; } = Environment.ProcessorCount;

    /// <summary>Renders allowed to wait for a slot; beyond this, requests get 429.</summary>
    public int MaxQueuedRenders { get; set; } = 100;

    /// <summary>Font directory; relative paths resolve against the app's base directory.</summary>
    public string FontDirectory { get; set; } = "fonts";
}
