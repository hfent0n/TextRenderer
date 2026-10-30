namespace TextRenderer.Service;

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

    /// <summary>Seconds sent in the <c>Retry-After</c> header of a 429 response.</summary>
    public int RetryAfterSeconds { get; set; } = 1;

    /// <summary>Font directory; relative paths resolve against the app's base directory.</summary>
    public string FontDirectory { get; set; } = "fonts";

    /// <summary>Problems that make these limits unusable; empty when they're sane (PLAN_PART_2 decision 15).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        void Require(bool ok, string message)
        {
            if (!ok) errors.Add(message);
        }

        Require(MaxTextLength > 0, $"{nameof(MaxTextLength)} must be positive.");
        var minFontSizeValid = MinFontSize > 0 && float.IsFinite(MinFontSize);
        Require(minFontSizeValid, $"{nameof(MinFontSize)} must be a positive number.");
        // Only compare against a valid minimum, so one bad value gives one message.
        Require(float.IsFinite(MaxFontSize) && (!minFontSizeValid || MaxFontSize >= MinFontSize),
            $"{nameof(MaxFontSize)} must be at least {nameof(MinFontSize)}.");
        Require(MaxPadding >= 0, $"{nameof(MaxPadding)} must not be negative.");
        Require(MaxCanvasPixels > 0, $"{nameof(MaxCanvasPixels)} must be positive.");
        Require(MaxRequestBodyBytes > 0, $"{nameof(MaxRequestBodyBytes)} must be positive.");
        Require(RenderTimeout > TimeSpan.Zero, $"{nameof(RenderTimeout)} must be positive.");
        Require(MaxConcurrentRenders > 0, $"{nameof(MaxConcurrentRenders)} must be positive.");
        Require(MaxQueuedRenders >= 0, $"{nameof(MaxQueuedRenders)} must not be negative.");
        Require(RetryAfterSeconds > 0, $"{nameof(RetryAfterSeconds)} must be positive.");
        Require(!string.IsNullOrWhiteSpace(FontDirectory), $"{nameof(FontDirectory)} is required.");
        return errors;
    }
}
