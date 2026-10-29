namespace TextRenderer.Rendering;

/// <summary>
/// Fills closed paths (as pixel-space edges) using a scanline sweep and the non-zero winding rule.
/// Currently one sample per pixel, at its centre: coverage is 0 or 1.
/// </summary>
public static class Rasterizer
{
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
        {
            var sampleY = top + row + 0.5f;

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
                    FillSpan(coverage, row * width, left, width, crossings[i].X, crossings[i + 1].X);
            }
        }

        return new CoverageMask(left, top, width, height, coverage);
    }

    /// <summary>Fills pixels whose centre lies in <c>[fromX, toX)</c>.</summary>
    private static void FillSpan(float[] coverage, int rowStart, int left, int width, float fromX, float toX)
    {
        var first = Math.Max(0, (int)MathF.Ceiling(fromX - 0.5f) - left);
        var last = Math.Min(width, (int)MathF.Ceiling(toX - 0.5f) - left);
        for (var x = first; x < last; x++)
            coverage[rowStart + x] = 1;
    }
}
