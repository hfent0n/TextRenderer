using Microsoft.Extensions.Configuration;
using TextRenderer.Service;

namespace TextRenderer.Tests.Rendering;

public class RenderLimitsTests
{
    private static RenderLimits Bind(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var limits = new RenderLimits();
        config.GetSection(RenderLimits.SectionName).Bind(limits);
        return limits;
    }

    [Fact]
    public void Defaults_match_the_plan()
    {
        var limits = new RenderLimits();

        Assert.Equal(500, limits.MaxTextLength);
        Assert.Equal((1f, 512f), (limits.MinFontSize, limits.MaxFontSize));
        Assert.Equal(256, limits.MaxPadding);
        Assert.Equal(16_000_000, limits.MaxCanvasPixels);
        Assert.Equal(16 * 1024, limits.MaxRequestBodyBytes);
        Assert.Equal(TimeSpan.FromSeconds(5), limits.RenderTimeout);
        Assert.Equal(Environment.ProcessorCount, limits.MaxConcurrentRenders);
        Assert.Equal(100, limits.MaxQueuedRenders);
        Assert.Equal(1, limits.RetryAfterSeconds);
        Assert.Equal("fonts", limits.FontDirectory);
    }

    [Fact]
    public void Binds_from_the_render_section()
    {
        var limits = Bind(new()
        {
            ["Render:MaxTextLength"] = "42",
            ["Render:MaxFontSize"] = "96.5",
            ["Render:MaxCanvasPixels"] = "1000000",
            ["Render:RenderTimeout"] = "00:00:00.250",
            ["Render:MaxConcurrentRenders"] = "3",
            ["Render:FontDirectory"] = "/srv/fonts",
        });

        Assert.Equal(42, limits.MaxTextLength);
        Assert.Equal(96.5f, limits.MaxFontSize);
        Assert.Equal(1_000_000, limits.MaxCanvasPixels);
        Assert.Equal(TimeSpan.FromMilliseconds(250), limits.RenderTimeout);
        Assert.Equal(3, limits.MaxConcurrentRenders);
        Assert.Equal("/srv/fonts", limits.FontDirectory);
    }

    [Fact]
    public void Unset_values_keep_their_defaults()
    {
        var limits = Bind(new() { ["Render:MaxTextLength"] = "42" });

        Assert.Equal(42, limits.MaxTextLength);
        Assert.Equal(16_000_000, limits.MaxCanvasPixels);
        Assert.Equal(TimeSpan.FromSeconds(5), limits.RenderTimeout);
    }

    [Fact]
    public void Missing_section_gives_all_defaults()
    {
        var limits = Bind(new() { ["Logging:LogLevel:Default"] = "Information" });

        Assert.Equal(500, limits.MaxTextLength);
        Assert.Equal("fonts", limits.FontDirectory);
    }

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.Empty(new RenderLimits().Validate());
    }

    public static TheoryData<string, string> InvalidLimits() => new()
    {
        { "MaxTextLength=0", "MaxTextLength must be positive." },
        { "MinFontSize=0", "MinFontSize must be a positive number." },
        { "MinFontSize=NaN", "MinFontSize must be a positive number." },
        { "MaxFontSize=0.5", "MaxFontSize must be at least MinFontSize." },
        { "MaxFontSize=Infinity", "MaxFontSize must be at least MinFontSize." },
        { "MaxPadding=-1", "MaxPadding must not be negative." },
        { "MaxCanvasPixels=0", "MaxCanvasPixels must be positive." },
        { "MaxRequestBodyBytes=0", "MaxRequestBodyBytes must be positive." },
        { "RenderTimeout=00:00:00", "RenderTimeout must be positive." },
        { "MaxConcurrentRenders=0", "MaxConcurrentRenders must be positive." },
        { "MaxQueuedRenders=-1", "MaxQueuedRenders must not be negative." },
        { "RetryAfterSeconds=0", "RetryAfterSeconds must be positive." },
        { "FontDirectory= ", "FontDirectory is required." },
    };

    [Theory]
    [MemberData(nameof(InvalidLimits))]
    public void Nonsensical_limits_are_reported(string setting, string expected)
    {
        var (key, value) = (setting.Split('=')[0], setting.Split('=')[1]);
        var limits = Bind(new() { [$"Render:{key}"] = value });

        Assert.Equal([expected], limits.Validate());
    }

    [Fact]
    public void Zero_padding_and_zero_queue_are_allowed()
    {
        Assert.Empty(Bind(new() { ["Render:MaxPadding"] = "0", ["Render:MaxQueuedRenders"] = "0" }).Validate());
    }
}
