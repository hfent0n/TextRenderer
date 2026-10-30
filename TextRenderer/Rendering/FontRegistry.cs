using System.Security.Cryptography;

namespace TextRenderer.Rendering;

/// <summary>Public description of a loaded font, as listed by <c>GET /fonts</c>.</summary>
public sealed record FontInfo(string Id, string Family, string Style, int UnitsPerEm);

/// <summary>
/// Every font in a directory, loaded once and cached, looked up by ID.
/// Loading fails fast: a missing or empty directory, an unreadable file or a duplicate ID throws.
/// </summary>
public sealed class FontRegistry
{
    private readonly Dictionary<string, IFontSource> _sources;
    private readonly Dictionary<string, string> _hashes;

    private FontRegistry(Dictionary<string, IFontSource> sources, Dictionary<string, string> hashes, IReadOnlyList<FontInfo> fonts)
    {
        _sources = sources;
        _hashes = hashes;
        Fonts = fonts;
    }

    /// <summary>Loaded fonts, ordered by ID.</summary>
    public IReadOnlyList<FontInfo> Fonts { get; }

    public bool TryGet(string id, out IFontSource font) => _sources.TryGetValue(id, out font!);

    /// <summary>SHA-256 of the font file, or empty for a font not loaded from a file.</summary>
    public string ContentHash(string id) => _hashes.GetValueOrDefault(id, "");

    /// <summary>A registry from already-loaded fonts, used as given (not wrapped in a cache). Mainly for tests.</summary>
    public static FontRegistry Create(IEnumerable<(FontInfo Info, IFontSource Source)> fonts)
    {
        var list = fonts.ToList();
        return new FontRegistry(
            list.ToDictionary(f => f.Info.Id, f => f.Source),
            [],
            list.Select(f => f.Info).OrderBy(f => f.Id, StringComparer.Ordinal).ToList());
    }

    /// <param name="directory">Absolute, or relative to the app's base directory.</param>
    public static FontRegistry Load(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory, directory);
        if (!Directory.Exists(path))
            throw new InvalidOperationException($"Font directory '{path}' does not exist.");

        var files = Directory.EnumerateFiles(path, "*.ttf").Order(StringComparer.Ordinal).ToList();
        if (files.Count == 0)
            throw new InvalidOperationException($"Font directory '{path}' contains no .ttf files.");

        var sources = new Dictionary<string, IFontSource>();
        var origins = new Dictionary<string, string>();
        var hashes = new Dictionary<string, string>();
        var fonts = new List<FontInfo>();

        foreach (var file in files)
        {
            SixLaborsFontSource font;
            try
            {
                font = new SixLaborsFontSource(file);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Could not load font '{file}': {ex.Message}", ex);
            }

            var id = IdFor(font.FamilyName, font.StyleName);
            if (origins.TryGetValue(id, out var existing))
                throw new InvalidOperationException(
                    $"Fonts '{existing}' and '{file}' both have ID '{id}' (family '{font.FamilyName}', style '{font.StyleName}').");

            origins[id] = file;
            sources[id] = new CachingFontSource(font);
            hashes[id] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            fonts.Add(new FontInfo(id, font.FamilyName, font.StyleName, font.Metrics.UnitsPerEm));
        }

        return new FontRegistry(sources, hashes, fonts.OrderBy(f => f.Id, StringComparer.Ordinal).ToList());
    }

    /// <summary>"DejaVu Sans Mono" + "Regular" → "dejavu-sans-mono"; "DejaVu Sans" + "Bold Italic" → "dejavu-sans-bold-italic".</summary>
    public static string IdFor(string family, string style)
    {
        var name = style == "Regular" ? family : $"{family} {style}";
        return string.Join('-', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
