using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using TextRenderer.Rendering;

namespace TextRenderer.Service;

/// <summary>
/// Recently rendered PNGs, bounded by total bytes. Rendering is deterministic, so identical requests
/// give identical images. Keys cover the build and font file too, so a change to either invalidates them.
/// </summary>
public sealed class RenderCache(FontRegistry fonts, RenderLimits limits) : IDisposable
{
    private static readonly Guid BuildId = typeof(RenderCache).Module.ModuleVersionId;

    private readonly MemoryCache _images = new(new MemoryCacheOptions { SizeLimit = limits.MaxCacheBytes });

    /// <summary>A strong ETag identifying the image this job produces, quoted as HTTP requires.</summary>
    public string ETagFor(RenderJob job)
    {
        var o = job.Options;
        // Text goes last, so it can't run into the fixed fields before it.
        var input = string.Create(CultureInfo.InvariantCulture,
            $"{BuildId}|{job.FontId}|{fonts.ContentHash(job.FontId)}|{o.FontSize:R}|{o.Padding}|{o.Foreground.ToArgb()}|{o.Background.ToArgb()}|{job.Text}");
        return $"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))}\"";
    }

    public bool TryGet(string etag, out byte[] image) => _images.TryGetValue(etag, out image!);

    public void Set(string etag, byte[] image) =>
        _images.Set(etag, image, new MemoryCacheEntryOptions { Size = image.Length });

    public void Dispose() => _images.Dispose();
}
