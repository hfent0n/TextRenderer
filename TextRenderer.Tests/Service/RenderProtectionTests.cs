using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Service;

/// <summary>
/// Concurrency limit and render timeout. A slow fake font makes requests really overlap:
/// each request looks every glyph up once in validation (no slot) and once in rendering (needs a slot).
/// </summary>
public class RenderProtectionTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient ClientWith(SlowFontSource font, params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings)
                b.UseSetting($"Render:{key}", value);
            b.ConfigureServices(s => s.AddSingleton(FontRegistry.Create(
                [(new FontInfo("slow", "Slow", "Regular", 64), font)])));
        }).CreateClient();

    private static Task<HttpResponseMessage> Post(HttpClient client, string text) =>
        client.PostAsync("/render", new StringContent(
            JsonSerializer.Serialize(new { text, font = "slow", fontSize = 16 }), Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Requests_beyond_the_queue_get_429_with_retry_after()
    {
        var client = ClientWith(new SlowFontSource(TimeSpan.FromMilliseconds(300)),
            ("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "0"), ("RetryAfterSeconds", "7"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Post(client, "A")));

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        var rejected = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        Assert.NotEmpty(rejected);
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.TooManyRequests, r.StatusCode.ToString()));

        var first = rejected[0];
        Assert.Equal("7", first.Headers.GetValues("Retry-After").Single());
        Assert.Equal("application/problem+json", first.Content.Headers.ContentType?.MediaType);
        var problem = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("The service is busy. Retry after 7 seconds.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Queued_requests_all_succeed_one_at_a_time()
    {
        var client = ClientWith(new SlowFontSource(TimeSpan.FromMilliseconds(200)),
            ("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "10"));

        var clock = Stopwatch.StartNew();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Post(client, "A")));
        clock.Stop();

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        // Validation runs in parallel (200ms), then 4 renders of 200ms each, one at a time.
        Assert.True(clock.ElapsedMilliseconds >= 900, $"took {clock.ElapsedMilliseconds}ms, so renders overlapped");
    }

    [Fact]
    public async Task Slow_render_gets_503_and_stops_early()
    {
        var font = new SlowFontSource(TimeSpan.FromMilliseconds(50));
        var client = ClientWith(font, ("RenderTimeout", "00:00:00.200"));

        var response = await Post(client, new string('A', 40));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Render timed out", problem.GetProperty("title").GetString());
        Assert.Contains("0.2 second limit", problem.GetProperty("detail").GetString());

        // 40 lookups in validation, then only a few of the 40 in rendering before the timeout.
        Assert.InRange(font.Calls, 41, 55);
    }

    [Fact]
    public async Task Time_spent_queued_does_not_count_towards_the_timeout()
    {
        // Each render takes ~300ms, inside the 700ms timeout. The third waits ~600ms in the queue first.
        var client = ClientWith(new SlowFontSource(TimeSpan.FromMilliseconds(300)),
            ("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "10"), ("RenderTimeout", "00:00:00.700"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Post(client, "A")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task Invalid_requests_get_their_400_without_waiting_for_a_slot()
    {
        var client = ClientWith(new SlowFontSource(TimeSpan.FromMilliseconds(500)),
            ("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "0"));

        var holdingTheSlot = Post(client, "A");   // validation 500ms, then render 500ms
        var invalid = await Post(client, "Z");    // not in the fake font

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await holdingTheSlot).StatusCode);
    }

    [Fact]
    public async Task A_slot_is_released_after_a_timeout()
    {
        var client = ClientWith(new SlowFontSource(TimeSpan.FromMilliseconds(100)),
            ("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "0"), ("RenderTimeout", "00:00:00.250"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Post(client, new string('A', 10))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "A")).StatusCode);
    }

    [Fact]
    public async Task A_client_that_gives_up_while_queued_frees_its_place()
    {
        var client = ClientWith(new SlowFontSource(TimeSpan.FromMilliseconds(400)),
            ("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "1"));

        var holdingTheSlot = Post(client, "A");
        await Task.Delay(500);                    // now past validation and rendering
        using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var abandoned = client.PostAsync("/render", new StringContent(
            """{ "text": "A", "font": "slow", "fontSize": 16 }""", Encoding.UTF8, "application/json"), giveUp.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        Assert.Equal(HttpStatusCode.OK, (await holdingTheSlot).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "A")).StatusCode);
    }
}
