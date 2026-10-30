using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TextRenderer.Tests.Service;

/// <summary>Concurrency limit and render timeout, using real renders large enough to overlap.</summary>
public class RenderProtectionTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Large = """{ "text": "The quick brown fox jumps over the lazy d", "font": "dejavu-serif", "fontSize": 512 }""";

    private HttpClient ClientWith(params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings)
                b.UseSetting($"Render:{key}", value);
        }).CreateClient();

    private static Task<HttpResponseMessage> Post(HttpClient client, string json) =>
        client.PostAsync("/render", new StringContent(json, Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Requests_beyond_the_queue_get_429_with_retry_after()
    {
        var client = ClientWith(("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "0"), ("RetryAfterSeconds", "7"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Post(client, Large))));

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.IsType<HttpResponseMessage>(responses.First(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal("7", rejected.Headers.GetValues("Retry-After").Single());
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("The service is busy. Retry after 7 seconds.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Slow_render_gets_503()
    {
        var client = ClientWith(("RenderTimeout", "00:00:00.001"));

        var response = await Post(client, Large);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Render timed out", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Time_spent_queued_does_not_count_towards_the_timeout()
    {
        var clock = Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.OK, (await Post(ClientWith(), Large)).StatusCode);
        var oneRender = clock.Elapsed;

        // The last of six waits for five renders, longer than the timeout of four.
        var timeout = oneRender * 4;
        var client = ClientWith(("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "10"), ("RenderTimeout", timeout.ToString("c")));

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Post(client, Large))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task Invalid_requests_get_their_400_while_the_only_slot_is_busy()
    {
        var client = ClientWith(("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "0"));

        var holdingTheSlot = Task.Run(() => Post(client, Large));
        await Task.Delay(50);
        var invalid = await Post(client, """{ "text": "Hi", "font": "nope" }""");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await holdingTheSlot).StatusCode);
    }

    [Fact]
    public async Task A_slot_is_released_after_a_timeout()
    {
        var client = ClientWith(("MaxConcurrentRenders", "1"), ("MaxQueuedRenders", "0"), ("RenderTimeout", "00:00:00.020"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Post(client, Large)).StatusCode);

        var next = await Post(client, """{ "text": "Hi", "font": "dejavu-sans", "fontSize": 12 }""");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, next.StatusCode);
    }
}
