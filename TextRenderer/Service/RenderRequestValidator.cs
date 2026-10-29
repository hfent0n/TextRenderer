using System.Buffers;
using System.Globalization;
using System.Text;
using TextRenderer.Rendering;
using static System.FormattableString;

namespace TextRenderer.Service;

/// <summary>A validated request, ready to render.</summary>
public sealed record RenderJob(string Text, string FontId, IFontSource Font, RenderOptions Options, IImageEncoder Encoder);

/// <summary>Either a <see cref="RenderJob"/> or validation errors keyed by JSON field name.</summary>
public sealed record RenderValidationResult(RenderJob? Job, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Job is not null;
}

/// <summary>
/// Validates a <see cref="RenderRequest"/> against the font registry and limits. Pure: no HTTP.
/// All field errors are collected; the canvas-size check runs last, only when everything else is valid.
/// </summary>
public sealed class RenderRequestValidator(FontRegistry fonts, RenderLimits limits)
{
    /// <summary>At most this many offending characters are listed (PLAN_PART_2 decision 12).</summary>
    public const int MaxCharacterErrors = 20;

    private static readonly IImageEncoder Png = new PngEncoder();
    private static readonly IImageEncoder Ppm = new PpmEncoder();

    public RenderValidationResult Validate(RenderRequest request)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
                errors[field] = list = [];
            list.Add(message);
        }

        IFontSource? font = null;
        if (string.IsNullOrEmpty(request.Font))
            Add("font", "Font is required.");
        else if (!fonts.TryGet(request.Font, out font))
            Add("font", $"Unknown font '{request.Font}'. See GET /fonts for available fonts.");

        if (request.Text is null)
            Add("text", "Text is required.");
        else if (request.Text.Length == 0)
            Add("text", "Text must not be empty.");
        else
            ValidateText(request.Text, request.Font!, font, message => Add("text", message));

        var fontSize = request.FontSize ?? new RenderOptions().FontSize;
        if (!(fontSize >= limits.MinFontSize && fontSize <= limits.MaxFontSize))
            Add("fontSize", Invariant($"Font size must be between {limits.MinFontSize} and {limits.MaxFontSize} pixels."));

        var padding = request.Padding ?? new RenderOptions().Padding;
        if (padding < 0 || padding > limits.MaxPadding)
            Add("padding", $"Padding must be between 0 and {limits.MaxPadding} pixels.");

        var foreground = new RenderOptions().Foreground;
        if (request.Foreground is not null && !ColourParser.TryParse(request.Foreground, out foreground))
            Add("foreground", "Colour must be #RRGGBB, e.g. #1A2B3C.");

        var background = new RenderOptions().Background;
        if (request.Background is not null && !ColourParser.TryParse(request.Background, out background))
            Add("background", "Colour must be #RRGGBB, e.g. #1A2B3C.");

        IImageEncoder? encoder = (request.Format ?? "png").ToLowerInvariant() switch
        {
            "png" => Png,
            "ppm" => Ppm,
            _ => null,
        };
        if (encoder is null)
            Add("format", "Format must be 'png' or 'ppm'.");

        if (errors.Count == 0)
        {
            var options = new RenderOptions
            {
                FontSize = fontSize,
                Padding = padding,
                Foreground = foreground,
                Background = background,
            };

            // Lay out before any canvas exists, so an oversized request costs almost nothing.
            var layout = TextLayout.Layout(request.Text!, font!, options);
            var pixels = (long)layout.Width * layout.Height;
            if (pixels > limits.MaxCanvasPixels)
                Add("canvas", Invariant($"The image would be {layout.Width}×{layout.Height} = {pixels:N0} pixels; ") +
                              Invariant($"the maximum is {limits.MaxCanvasPixels:N0}. Use shorter text or a smaller font size."));
            else
                return new RenderValidationResult(new RenderJob(request.Text!, request.Font!, font!, options, encoder!), new Dictionary<string, string[]>());
        }

        return new RenderValidationResult(null, errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }

    private void ValidateText(string text, string fontId, IFontSource? font, Action<string> add)
    {
        var length = 0;
        var reported = new HashSet<int>();
        var hidden = 0;
        var unpairedSurrogate = false;

        void Report(int codepoint, string message)
        {
            if (!reported.Add(codepoint))
                return;
            if (reported.Count <= MaxCharacterErrors)
                add(message);
            else
                hidden++;
        }

        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            length++;
            // Decode explicitly: EnumerateRunes would silently turn an unpaired surrogate into U+FFFD,
            // which DejaVu has a glyph for.
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            remaining = remaining[consumed..];

            if (status != OperationStatus.Done)
            {
                unpairedSurrogate = true;
                continue;
            }

            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control:
                    Report(rune.Value, $"Control character {Describe(rune, printable: false)} is not allowed.");
                    break;
                case UnicodeCategory.Format:
                    Report(rune.Value, $"Invisible formatting character {Describe(rune, printable: false)} is not allowed.");
                    break;
                default:
                    if (font is not null && !font.HasGlyph(rune.Value))
                        Report(rune.Value, $"Font '{fontId}' has no glyph for {Describe(rune, printable: true)}.");
                    break;
            }
        }

        if (hidden > 0)
            add($"…and {hidden} more unsupported character{(hidden == 1 ? "" : "s")}.");
        if (unpairedSurrogate)
            add("Text contains an unpaired surrogate, which is not valid Unicode.");
        if (length > limits.MaxTextLength)
            add($"Text is {length} characters long; the maximum is {limits.MaxTextLength}.");
    }

    private static string Describe(Rune rune, bool printable) =>
        printable ? $"U+{rune.Value:X4} '{rune}'" : $"U+{rune.Value:X4}";
}
