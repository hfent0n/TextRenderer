using System.Diagnostics.Metrics;

namespace TextRenderer.Service;

/// <summary>
/// One structured log entry per <c>POST /render</c>, plus render metrics.
/// Never logs the text, and only logs validated values, so nothing a caller typed reaches the logs.
/// </summary>
public sealed partial class RenderTelemetry
{
    public const string MeterName = "TextRenderer";
    public const string RenderDurationMetric = "textrenderer.render.duration";
    public const string QueueDurationMetric = "textrenderer.render.queue.duration";
    public const string CacheHitsMetric = "textrenderer.render.cache.hits";

    private readonly ILogger<RenderTelemetry> _logger;
    private readonly Histogram<double> _renderDuration;
    private readonly Histogram<double> _queueDuration;
    private readonly Counter<long> _cacheHits;

    public RenderTelemetry(ILogger<RenderTelemetry> logger, IMeterFactory meters)
    {
        _logger = logger;
        var meter = meters.Create(MeterName);
        _renderDuration = meter.CreateHistogram<double>(RenderDurationMetric, "s", "Time spent rendering and encoding, excluding the queue.");
        _queueDuration = meter.CreateHistogram<double>(QueueDurationMetric, "s", "Time spent waiting for a render slot.");
        _cacheHits = meter.CreateCounter<long>(CacheHitsMetric, description: "Requests answered without rendering.");
    }

    public void Rendered(RenderJob job, int width, int height, TimeSpan queued, TimeSpan rendered)
    {
        Record(job, "rendered", queued, rendered);
        LogRendered(job.FontId, job.Options.FontSize, TextLength(job), width, height, rendered.TotalMilliseconds, queued.TotalMilliseconds);
    }

    public void TimedOut(RenderJob job, TimeSpan queued, TimeSpan rendered)
    {
        Record(job, "timed_out", queued, rendered);
        LogTimedOut(job.FontId, job.Options.FontSize, TextLength(job), rendered.TotalMilliseconds, queued.TotalMilliseconds);
    }

    public void Overloaded(RenderJob job, TimeSpan queued)
    {
        _queueDuration.Record(queued.TotalSeconds);
        LogOverloaded(job.FontId, job.Options.FontSize, TextLength(job), queued.TotalMilliseconds);
    }

    public void Rejected(int status, int errorCount) => LogRejected(status, errorCount);

    public void CacheHit(RenderJob job, int status)
    {
        _cacheHits.Add(1, new KeyValuePair<string, object?>("status", status));
        LogCacheHit(job.FontId, job.Options.FontSize, TextLength(job), status);
    }

    private void Record(RenderJob job, string outcome, TimeSpan queued, TimeSpan rendered)
    {
        _queueDuration.Record(queued.TotalSeconds);
        _renderDuration.Record(rendered.TotalSeconds,
            new KeyValuePair<string, object?>("font", job.FontId),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    private static int TextLength(RenderJob job) => job.Text.EnumerateRunes().Count();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Rendered {Font} at {FontSize}px, {TextLength} characters, {Width}x{Height} in {RenderMs:0.0}ms, queued {QueueMs:0.0}ms")]
    private partial void LogRendered(string font, float fontSize, int textLength, int width, int height, double renderMs, double queueMs);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Render timed out: {Font} at {FontSize}px, {TextLength} characters, after {RenderMs:0.0}ms, queued {QueueMs:0.0}ms")]
    private partial void LogTimedOut(string font, float fontSize, int textLength, double renderMs, double queueMs);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Render rejected, queue full: {Font} at {FontSize}px, {TextLength} characters, waited {QueueMs:0.0}ms")]
    private partial void LogOverloaded(string font, float fontSize, int textLength, double queueMs);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Answered without rendering: {Font} at {FontSize}px, {TextLength} characters, status {Status}")]
    private partial void LogCacheHit(string font, float fontSize, int textLength, int status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request rejected with {Status}, error count {ErrorCount}")]
    private partial void LogRejected(int status, int errorCount);
}
