using TextRenderer.Rendering;
using TextRenderer.Service;

namespace TextRenderer.Tests.Rendering;

public sealed class FontRegistryTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("font-registry-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string CopyFont(string fontFile, string? asName = null)
    {
        var target = Path.Combine(_tempDir, asName ?? fontFile);
        File.Copy(TestPaths.Font(fontFile), target);
        return target;
    }

    [Fact]
    public void Loads_the_repo_fonts_from_the_default_directory_in_build_output()
    {
        var registry = FontRegistry.Load(new RenderLimits().FontDirectory);

        Assert.Equal(
            [
                new FontInfo("dejavu-sans", "DejaVu Sans", "Regular", 2048),
                new FontInfo("dejavu-sans-mono", "DejaVu Sans Mono", "Regular", 2048),
                new FontInfo("dejavu-serif", "DejaVu Serif", "Regular", 2048),
            ],
            registry.Fonts);
    }

    [Theory]
    [InlineData("dejavu-sans")]
    [InlineData("dejavu-sans-mono")]
    [InlineData("dejavu-serif")]
    public void Looks_up_fonts_by_id(string id)
    {
        var registry = FontRegistry.Load("fonts");

        Assert.True(registry.TryGet(id, out var font));
        Assert.True(font.GetGlyph('A').AdvanceWidth > 0);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("DejaVu Sans")]   // IDs are exact, not family names
    [InlineData("DEJAVU-SANS")]   // and case-sensitive
    [InlineData("")]
    public void Unknown_id_is_not_found(string id)
    {
        Assert.False(FontRegistry.Load("fonts").TryGet(id, out _));
    }

    [Fact]
    public void Registered_fonts_are_cached()
    {
        FontRegistry.Load("fonts").TryGet("dejavu-sans", out var font);

        Assert.IsType<CachingFontSource>(font);
        Assert.Same(font.GetGlyph('Q'), font.GetGlyph('Q'));
    }

    [Fact]
    public void Absolute_directory_is_used_as_is()
    {
        CopyFont("DejaVuSerif.ttf");

        var registry = FontRegistry.Load(_tempDir);

        Assert.Equal("dejavu-serif", Assert.Single(registry.Fonts).Id);
    }

    [Fact]
    public void Id_comes_from_metadata_not_the_file_name()
    {
        CopyFont("DejaVuSansMono.ttf", asName: "whatever-name.ttf");

        Assert.Equal("dejavu-sans-mono", Assert.Single(FontRegistry.Load(_tempDir).Fonts).Id);
    }

    [Theory]
    [InlineData("DejaVu Sans", "Regular", "dejavu-sans")]
    [InlineData("DejaVu Sans Mono", "Regular", "dejavu-sans-mono")]
    [InlineData("DejaVu Sans", "Bold", "dejavu-sans-bold")]
    [InlineData("DejaVu Serif", "Bold Italic", "dejavu-serif-bold-italic")]
    [InlineData("  Odd   Spacing ", "Italic", "odd-spacing-italic")]
    public void Id_is_family_plus_non_regular_style_hyphenated(string family, string style, string expected)
    {
        Assert.Equal(expected, FontRegistry.IdFor(family, style));
    }

    [Fact]
    public void Missing_directory_fails_fast()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FontRegistry.Load(Path.Combine(_tempDir, "missing")));

        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Directory_without_fonts_fails_fast()
    {
        File.WriteAllText(Path.Combine(_tempDir, "LICENSE.txt"), "not a font");

        var ex = Assert.Throws<InvalidOperationException>(() => FontRegistry.Load(_tempDir));

        Assert.Contains("contains no", ex.Message);
    }

    [Fact]
    public void Unreadable_font_fails_fast_and_names_the_file()
    {
        CopyFont("DejaVuSans.ttf");
        var broken = Path.Combine(_tempDir, "Broken.ttf");
        File.WriteAllBytes(broken, [0x00, 0x01, 0x02, 0x03, 0x04]);

        var ex = Assert.Throws<InvalidOperationException>(() => FontRegistry.Load(_tempDir));

        Assert.Contains(broken, ex.Message);
    }

    [Fact]
    public void Duplicate_ids_fail_fast_and_name_both_files()
    {
        var first = CopyFont("DejaVuSans.ttf", "A.ttf");
        var second = CopyFont("DejaVuSans.ttf", "B.ttf");

        var ex = Assert.Throws<InvalidOperationException>(() => FontRegistry.Load(_tempDir));

        Assert.Contains("dejavu-sans", ex.Message);
        Assert.Contains(first, ex.Message);
        Assert.Contains(second, ex.Message);
    }

    [Fact]
    public void Non_font_files_are_ignored()
    {
        CopyFont("DejaVuSans.ttf");
        File.WriteAllText(Path.Combine(_tempDir, "LICENSE.txt"), "licence text");

        Assert.Single(FontRegistry.Load(_tempDir).Fonts);
    }
}
