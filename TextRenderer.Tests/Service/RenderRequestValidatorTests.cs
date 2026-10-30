using System.Drawing;
using TextRenderer.Rendering;
using TextRenderer.Service;

namespace TextRenderer.Tests.Service;

public class RenderRequestValidatorTests
{
    private static readonly FontRegistry Fonts = FontRegistry.Load("fonts");

    private static readonly RenderRequest Valid = new() { Text = "Hello", Font = "dejavu-sans" };

    private static RenderValidationResult Validate(RenderRequest request, RenderLimits? limits = null) =>
        new RenderRequestValidator(Fonts, limits ?? new RenderLimits()).Validate(request);

    private static string[] ErrorsFor(RenderValidationResult result, string field)
    {
        Assert.False(result.IsValid);
        Assert.True(result.Errors.TryGetValue(field, out var errors), $"no '{field}' error; got: {string.Join(", ", result.Errors.Keys)}");
        return errors;
    }

    // --- Valid requests ---

    [Fact]
    public void Minimal_request_uses_defaults()
    {
        var result = Validate(Valid);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        var job = result.Job!;
        Assert.Equal(("Hello", "dejavu-sans"), (job.Text, job.FontId));
        Assert.Equal(new RenderOptions(), job.Options);
        Assert.Equal(5, job.Layout.Glyphs.Count);
    }

    [Fact]
    public void All_fields_are_applied()
    {
        var result = Validate(Valid with
        {
            Font = "dejavu-serif", FontSize = 20.5f, Padding = 0,
            Foreground = "#112233", Background = "#aabbcc",
        });

        var job = result.Job!;
        Assert.Equal((20.5f, 0), (job.Options.FontSize, job.Options.Padding));
        Assert.Equal(Color.FromArgb(0x11, 0x22, 0x33).ToArgb(), job.Options.Foreground.ToArgb());
        Assert.Equal(Color.FromArgb(0xAA, 0xBB, 0xCC).ToArgb(), job.Options.Background.ToArgb());
    }

    [Theory]
    [InlineData("The quick brown fox jumps over the lazy dog.")]
    [InlineData("Ångström café naïve façade Øresund")]
    [InlineData("0123456789 +-×÷=<> $€£¥")]
    [InlineData(" ")]                // whitespace-only is allowed: it renders blank
    [InlineData("😀")]              // DejaVu Sans has this one, as a surrogate pair
    public void Text_the_font_supports_is_valid(string text)
    {
        Assert.True(Validate(Valid with { Text = text }).IsValid);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(512f)]
    public void Font_size_limits_are_inclusive(float size)
    {
        Assert.True(Validate(Valid with { FontSize = size }).IsValid);
    }

    // --- font ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Font_is_required(string? font)
    {
        Assert.Equal(["Font is required."], ErrorsFor(Validate(Valid with { Font = font }), "font"));
    }

    [Fact]
    public void Unknown_font_is_rejected_with_a_pointer_to_the_list()
    {
        var errors = ErrorsFor(Validate(Valid with { Font = "comic-sans" }), "font");

        Assert.Equal(["Unknown font 'comic-sans'. See GET /fonts for available fonts."], errors);
    }

    // --- text ---

    [Fact]
    public void Text_is_required()
    {
        Assert.Equal(["Text is required."], ErrorsFor(Validate(Valid with { Text = null }), "text"));
    }

    [Fact]
    public void Text_must_not_be_empty()
    {
        Assert.Equal(["Text must not be empty."], ErrorsFor(Validate(Valid with { Text = "" }), "text"));
    }

    [Fact]
    public void Text_length_is_counted_in_code_points()
    {
        var limits = new RenderLimits { MaxTextLength = 3 };

        Assert.True(Validate(Valid with { Text = "A😀B" }, limits).IsValid);  // 4 chars, 3 code points
        Assert.Equal(["Text is 4 characters long; the maximum is 3."],
            ErrorsFor(Validate(Valid with { Text = "ABCD" }, limits), "text"));
    }

    [Fact]
    public void Unsupported_characters_are_listed_with_code_points()
    {
        var errors = ErrorsFor(Validate(Valid with { Text = "Hi 中 💩" }), "text");

        Assert.Equal(
            [
                "Font 'dejavu-sans' has no glyph for U+4E2D '中'.",
                "Font 'dejavu-sans' has no glyph for U+1F4A9 '💩'.",
            ],
            errors);
    }

    [Fact]
    public void Support_is_checked_against_the_chosen_font()
    {
        // DejaVu Sans has 😀; DejaVu Serif doesn't.
        Assert.True(Validate(Valid with { Text = "😀", Font = "dejavu-sans" }).IsValid);
        Assert.Single(ErrorsFor(Validate(Valid with { Text = "😀", Font = "dejavu-serif" }), "text"));
    }

    [Fact]
    public void Each_offending_character_is_reported_once()
    {
        var errors = ErrorsFor(Validate(Valid with { Text = "中中中a中" }), "text");

        Assert.Single(errors);
    }

    [Fact]
    public void Character_errors_are_capped_at_20_then_counted()
    {
        var text = string.Concat(Enumerable.Range(0, 25).Select(i => (char)(0x4E00 + i)));  // 25 distinct CJK characters

        var errors = ErrorsFor(Validate(Valid with { Text = text }), "text");

        Assert.Equal(21, errors.Length);
        Assert.All(errors[..20], e => Assert.StartsWith("Font 'dejavu-sans' has no glyph for U+4E", e));
        Assert.Equal("…and 5 more unsupported characters.", errors[20]);
    }

    [Theory]
    [InlineData("a\nb", "U+000A")]
    [InlineData("a\tb", "U+0009")]
    [InlineData("a\rb", "U+000D")]
    [InlineData("a\0b", "U+0000")]
    [InlineData("a\u007Fb", "U+007F")]
    public void Control_characters_are_rejected(string text, string codepoint)
    {
        Assert.Equal([$"Control character {codepoint} is not allowed."], ErrorsFor(Validate(Valid with { Text = text }), "text"));
    }

    [Theory]
    [InlineData("a\u200Bb", "U+200B")]   // zero-width space
    [InlineData("a\u200Db", "U+200D")]   // zero-width joiner
    [InlineData("\uFEFFab", "U+FEFF")]   // byte-order mark
    [InlineData("a\u202Eb", "U+202E")]   // right-to-left override
    public void Invisible_formatting_characters_are_rejected(string text, string codepoint)
    {
        Assert.Equal([$"Invisible formatting character {codepoint} is not allowed."], ErrorsFor(Validate(Valid with { Text = text }), "text"));
    }

    // Strings are built inside the test: xUnit serializes InlineData strings, and an unpaired
    // surrogate doesn't survive that (it arrives as U+FFFD, which is valid).
    [Theory]
    [InlineData("a", 0xD800, "b")]   // lone high surrogate
    [InlineData("a", 0xDC00, "b")]   // lone low surrogate
    [InlineData("ab", 0xD83D, "")]   // high surrogate at the end
    [InlineData("", 0xDE00, "ab")]   // low surrogate at the start
    public void Unpaired_surrogates_are_rejected_not_turned_into_replacement_characters(string before, int surrogate, string after)
    {
        var text = before + (char)surrogate + after;

        Assert.Equal(["Text contains an unpaired surrogate, which is not valid Unicode."],
            ErrorsFor(Validate(Valid with { Text = text }), "text"));
    }

    [Fact]
    public void Real_replacement_character_is_allowed_when_the_font_has_it()
    {
        Assert.True(Validate(Valid with { Text = "a�b" }).IsValid);
    }

    [Fact]
    public void Text_problems_are_all_reported_together()
    {
        var limits = new RenderLimits { MaxTextLength = 4 };

        var errors = ErrorsFor(Validate(Valid with { Text = "a\n中\u200Bb\uD800" }, limits), "text");

        Assert.Equal(
            [
                "Control character U+000A is not allowed.",
                "Font 'dejavu-sans' has no glyph for U+4E2D '中'.",
                "Invisible formatting character U+200B is not allowed.",
                "Text contains an unpaired surrogate, which is not valid Unicode.",
                "Text is 6 characters long; the maximum is 4.",
            ],
            errors);
    }

    [Fact]
    public void With_an_unknown_font_only_font_independent_text_rules_apply()
    {
        var result = Validate(Valid with { Font = "nope", Text = "中\n" });

        Assert.Single(ErrorsFor(result, "font"));
        Assert.Equal(["Control character U+000A is not allowed."], ErrorsFor(result, "text"));
    }

    // --- fontSize, padding, colours, format ---

    [Theory]
    [InlineData(0f)]
    [InlineData(0.99f)]
    [InlineData(512.01f)]
    [InlineData(-10f)]
    public void Font_size_outside_limits_is_rejected(float size)
    {
        Assert.Equal(["Font size must be between 1 and 512 pixels."], ErrorsFor(Validate(Valid with { FontSize = size }), "fontSize"));
    }

    [Fact]
    public void Font_size_message_uses_configured_limits_with_invariant_formatting()
    {
        var limits = new RenderLimits { MinFontSize = 2.5f, MaxFontSize = 96.5f };

        Assert.Equal(["Font size must be between 2.5 and 96.5 pixels."],
            ErrorsFor(Validate(Valid with { FontSize = 100 }, limits), "fontSize"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(257)]
    public void Padding_outside_limits_is_rejected(int padding)
    {
        Assert.Equal(["Padding must be between 0 and 256 pixels."], ErrorsFor(Validate(Valid with { Padding = padding }), "padding"));
    }

    [Theory]
    [InlineData("foreground")]
    [InlineData("background")]
    public void Bad_colours_are_rejected_per_field(string field)
    {
        var request = field == "foreground" ? Valid with { Foreground = "red" } : Valid with { Background = "#FFF" };

        Assert.Equal(["Colour must be #RRGGBB, e.g. #1A2B3C."], ErrorsFor(Validate(request), field));
    }

    // --- canvas size ---

    [Fact]
    public void Oversized_canvas_is_rejected_with_its_dimensions()
    {
        // DejaVu Sans "H" at 100px with 8px padding: 76×133 = 10,108 pixels.
        var limits = new RenderLimits { MaxCanvasPixels = 10_000 };

        var errors = ErrorsFor(Validate(Valid with { Text = "H", FontSize = 100 }, limits), "canvas");

        Assert.Equal(["The image would be 92×133 = 12,236 pixels; the maximum is 10,000. Use shorter text or a smaller font size."], errors);
    }

    [Fact]
    public void Default_limits_stop_the_worst_case_request()
    {
        // 500 W's at the maximum size: every other limit allows it, the canvas limit doesn't.
        var errors = ErrorsFor(Validate(Valid with { Text = new string('W', 500), FontSize = 512, Padding = 256 }), "canvas");

        Assert.Single(errors);
    }

    [Fact]
    public void Canvas_is_only_checked_once_everything_else_is_valid()
    {
        var limits = new RenderLimits { MaxCanvasPixels = 1 };

        var result = Validate(Valid with { Padding = -1 }, limits);

        Assert.Equal(["padding"], result.Errors.Keys);
    }

    // --- combined ---

    [Fact]
    public void Every_invalid_field_is_reported_together()
    {
        var result = Validate(new RenderRequest
        {
            Text = "", Font = "nope", FontSize = 0, Padding = -1,
            Foreground = "x", Background = "y",
        });

        Assert.Null(result.Job);
        Assert.Equal(["background", "font", "fontSize", "foreground", "padding", "text"], result.Errors.Keys.Order());
    }
}
