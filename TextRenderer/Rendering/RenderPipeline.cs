namespace TextRenderer.Rendering;

/// <summary>
/// Renders text in one font onto a <see cref="Canvas"/>: layout, then for each glyph flatten → rasterize → blend.
/// Encoding the canvas is left to the caller.
/// </summary>
public sealed class RenderPipeline(IFontSource font)
{
    public Canvas Render(string text, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        var layout = TextLayout.Layout(text, font, options);
        var canvas = new Canvas(layout.Width, layout.Height, options.Background);

        foreach (var (glyph, penX) in layout.Glyphs)
        {
            if (glyph.Contours.Count == 0)
                continue;

            var edges = PathFlattener.Flatten(glyph, layout.Scale, penX, layout.Baseline);
            var mask = Rasterizer.Rasterize(edges, canvas.Width, canvas.Height);
            canvas.Blend(mask, options.Foreground);
        }

        return canvas;
    }
}
