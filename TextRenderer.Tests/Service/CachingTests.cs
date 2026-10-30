using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using TextRenderer.Rendering;
using TextRenderer.Service;

namespace TextRenderer.Tests.Service;

public class CachingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Hello = """{ "text": "Hello", "font": "dejavu-sans" }""";

    private WebApplicationFactory<Program> App(params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings)
                b.UseSetting($"Render:{key}", value);
        });

    private static MetricCollector<double> Renders(WebApplicationFactory<Program> app) =>
        new(app.Services.GetRequiredService<IMeterFactory>(), RenderTelemetry.MeterName, RenderTelemetry.RenderDurationMetric);

    private static Task<HttpResponseMessage> Post(HttpClient client, string json, string? ifNoneMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/render") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (ifNoneMatch is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        return client.SendAsync(request);
    }

    private static string ETag(HttpResponseMessage response) => response.Headers.ETag!.Tag;

    [Fact]
    public async Task A_repeat_request_is_served_without_rendering()
    {
        using var app = App();
        using var renders = Renders(app);
        var client = app.CreateClient();

        var first = await Post(client, Hello);
        var second = await Post(client, Hello);

        Assert.Equal(ETag(first), ETag(second));
        Assert.Equal(await first.Content.ReadAsByteArrayAsync(), await second.Content.ReadAsByteArrayAsync());
        Assert.Single(renders.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task A_matching_if_none_match_gets_304_and_a_stale_one_gets_the_image()
    {
        using var app = App();
        var client = app.CreateClient();
        var etag = ETag(await Post(client, Hello));

        var matched = await Post(client, Hello, etag);
        var stale = await Post(client, Hello, "\"stale\"");

        Assert.Equal(HttpStatusCode.NotModified, matched.StatusCode);
        Assert.Empty(await matched.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
    }

    [Fact]
    public async Task Each_option_changes_the_etag_but_spelling_out_a_default_does_not()
    {
        var client = App().CreateClient();
        var baseline = ETag(await Post(client, Hello));

        string[] different =
        [
            """{ "text": "Hello!", "font": "dejavu-sans" }""",
            """{ "text": "Hello", "font": "dejavu-serif" }""",
            """{ "text": "Hello", "font": "dejavu-sans", "fontSize": 47 }""",
            """{ "text": "Hello", "font": "dejavu-sans", "padding": 7 }""",
            """{ "text": "Hello", "font": "dejavu-sans", "foreground": "#000001" }""",
            """{ "text": "Hello", "font": "dejavu-sans", "background": "#FFFFFE" }""",
        ];
        foreach (var json in different)
            Assert.NotEqual(baseline, ETag(await Post(client, json)));

        var explicitDefaults = """{ "text": "Hello", "font": "dejavu-sans", "fontSize": 48, "padding": 8, "foreground": "#000000", "background": "#ffffff" }""";
        Assert.Equal(baseline, ETag(await Post(client, explicitDefaults)));
    }

    [Fact]
    public void Each_font_file_has_its_own_content_hash()
    {
        using var app = App();
        var fonts = app.Services.GetRequiredService<FontRegistry>();

        Assert.NotEqual(fonts.ContentHash("dejavu-sans"), fonts.ContentHash("dejavu-serif"));
        Assert.Equal(64, fonts.ContentHash("dejavu-sans").Length);
    }

    [Fact]
    public async Task Cached_answers_need_no_render_slot()
    {
        var client = App(("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "0")).CreateClient();
        await Post(client, Hello);

        var large = """{ "text": "The quick brown fox jumps over the lazy d", "font": "dejavu-serif", "fontSize": 512 }""";
        var holdingTheSlot = Task.Run(() => Post(client, large));
        await Task.Delay(50);
        var cached = await Post(client, Hello);

        Assert.Equal(HttpStatusCode.OK, cached.StatusCode);
        await holdingTheSlot;
    }

    [Fact]
    public async Task An_image_over_the_size_limit_is_not_kept()
    {
        using var app = App(("MaxCacheBytes", "1"));
        using var renders = Renders(app);
        var client = app.CreateClient();

        await Post(client, Hello);
        await Post(client, Hello);

        Assert.Equal(2, renders.GetMeasurementSnapshot().Count);
    }

    [Fact]
    public async Task Failures_carry_no_etag()
    {
        var client = App(("RenderTimeout", "00:00:00.001")).CreateClient();
        var large = """{ "text": "The quick brown fox jumps over the lazy d", "font": "dejavu-serif", "fontSize": 512 }""";

        var response = await Post(client, large);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(response.Headers.ETag);
    }
}
