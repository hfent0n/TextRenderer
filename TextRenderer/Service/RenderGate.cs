using System.Threading.RateLimiting;
using TextRenderer.Rendering;

namespace TextRenderer.Service;

/// <summary>
/// Caps how many renders run at once, queueing the rest oldest-first up to a limit (PLAN_PART_2 decision 18).
/// Wraps render + encode only, not body reading or validation.
/// </summary>
public sealed class RenderGate(RenderLimits limits) : IDisposable
{
    private readonly ConcurrencyLimiter _limiter = new(new ConcurrencyLimiterOptions
    {
        PermitLimit = limits.MaxConcurrentRenders,
        QueueLimit = limits.MaxQueuedRenders,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    /// <summary>
    /// Waits for a render slot. The lease's <see cref="RateLimitLease.IsAcquired"/> is false when the queue is full.
    /// Cancelling (e.g. the client disconnects) removes the request from the queue.
    /// </summary>
    public ValueTask<RateLimitLease> AcquireAsync(CancellationToken cancellationToken) =>
        _limiter.AcquireAsync(1, cancellationToken);

    public void Dispose() => _limiter.Dispose();
}
