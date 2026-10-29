namespace TextRenderer.Rendering;

/// <summary>
/// Fills closed paths (as pixel-space edges) using a scanline sweep and the non-zero winding rule.
/// Anti-aliased by supersampling: each pixel is sampled on a <see cref="Samples"/> × <see cref="Samples"/>
/// grid (each sample at the centre of its sub-cell), and coverage is the fraction of samples inside.
/// </summary>
public static class Rasterizer
{
    /// <summary>Samples per pixel along each axis.</summary>
    public const int Samples = 4;

    private const float SampleCoverage = 1f / (Samples * Samples);

    /// <summary>
    /// Rasterizes <paramref name="edges"/> into a mask covering their pixel bounding box,
    /// clipped to <c>[0, clipWidth) × [0, clipHeight)</c>.
    /// </summary>
    public static CoverageMask Rasterize(IReadOnlyList<Edge> edges, int clipWidth, int clipHeight)
    {
        if (edges.Count == 0)
            return CoverageMask.Empty;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var e in edges)
        {
            minX = Math.Min(minX, Math.Min(e.X0, e.X1));
            maxX = Math.Max(maxX, Math.Max(e.X0, e.X1));
            minY = Math.Min(minY, Math.Min(e.Y0, e.Y1));
            maxY = Math.Max(maxY, Math.Max(e.Y0, e.Y1));
        }

        var left = Math.Max(0, (int)MathF.Floor(minX));
        var top = Math.Max(0, (int)MathF.Floor(minY));
        var right = Math.Min(clipWidth, (int)MathF.Ceiling(maxX));
        var bottom = Math.Min(clipHeight, (int)MathF.Ceiling(maxY));
        if (right <= left || bottom <= top)
            return CoverageMask.Empty;

        int width = right - left, height = bottom - top;
        var coverage = new float[width * height];
        var crossings = new List<(float X, int Winding)>();

        for (var row = 0; row < height; row++)
        for (var sub = 0; sub < Samples; sub++)
        {
            var sampleY = top + row + (sub + 0.5f) / Samples;

            crossings.Clear();
            foreach (var e in edges)
            {
                // Half-open [low, high) so a vertex shared by two edges is counted once.
                var (low, high) = e.Y0 < e.Y1 ? (e.Y0, e.Y1) : (e.Y1, e.Y0);
                if (sampleY < low || sampleY >= high)
                    continue;

                var x = e.X0 + (sampleY - e.Y0) * (e.X1 - e.X0) / (e.Y1 - e.Y0);
                crossings.Add((x, e.Winding));
            }
            crossings.Sort((a, b) => a.X.CompareTo(b.X));

            var winding = 0;
            for (var i = 0; i < crossings.Count - 1; i++)
            {
                winding += crossings[i].Winding;
                if (winding != 0)
                    AccumulateSpan(coverage, row * width, left, width, crossings[i].X, crossings[i + 1].X);
            }
        }

        return new CoverageMask(left, top, width, height, coverage);
    }

    /// <summary>
    /// Adds coverage for every horizontal sample whose position lies in <c>[fromX, toX)</c>.
    /// Sample <c>k</c> (counted across the whole row) sits at <c>(k + 0.5) / Samples</c>.
    /// </summary>
    private static void AccumulateSpan(float[] coverage, int rowStart, int left, int width, float fromX, float toX)
    {
        var first = Math.Max(left * Samples, (int)MathF.Ceiling(fromX * Samples - 0.5f));
        var end = Math.Min((left + width) * Samples, (int)MathF.Ceiling(toX * Samples - 0.5f));
        for (var k = first; k < end; k++)
            coverage[rowStart + k / Samples - left] += SampleCoverage;
    }
}
