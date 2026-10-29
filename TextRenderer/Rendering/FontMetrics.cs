namespace TextRenderer.Rendering;

/// <summary>Font-wide vertical metrics, in font units (Y-up, baseline at 0).</summary>
public sealed record FontMetrics(int UnitsPerEm, int Ascender, int Descender, int LineGap);
