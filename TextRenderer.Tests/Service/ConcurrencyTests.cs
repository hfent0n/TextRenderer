using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TextRenderer.Rendering;
using TextRenderer.Service;

namespace TextRenderer.Tests.Service;

/// <summary>
/// The real renderer and real fonts under parallel load: output must not depend on what else is running.
/// Each parallel run uses a fresh host, so glyph caches start cold and threads race to fill them.
/// </summary>
public class ConcurrencyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const int ParallelRequests = 200;

    private static readonly string[] Fonts = ["dejavu-sans", "dejavu-sans-mono", "dejavu-serif"];
    private static readonly int[] Sizes = [12, 32, 96];
    private static readonly string[] Texts =
    [
        "Hello, World!",
        "The quick brown fox jumps over the lazy dog.",
        "0123456789 +-×÷=<> $€£¥",
        "Ångström café naïve façade",
    ];

    /// <summary>Every font × size × text combination, repeated to reach the request count.</summary>
    private static List<object> Requests()
    {
        var distinct = (from font in Fonts from fontSize in Sizes from text in Texts select (object)new { text, font, fontSize }).ToList();
        return Enumerable.Range(0, ParallelRequests).Select(i => distinct[i % distinct.Count]).ToList();
    }

    private HttpClient FreshClient(params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings)
                b.UseSetting($"Render:{key}", value);
        }).CreateClient();

    private async Task<List<byte[]>> RenderOneAtATime(List<object> requests)
    {
        var client = FreshClient();
        var results = new List<byte[]>();
        foreach (var request in requests)
        {
            var response = await client.PostAsJsonAsync("/render", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            results.Add(await response.Content.ReadAsByteArrayAsync());
        }
        return results;
    }

    [Fact]
    public async Task Parallel_requests_match_the_same_requests_made_one_at_a_time()
    {
        var requests = Requests();
        var expected = await RenderOneAtATime(requests);

        var client = FreshClient(("MaxQueuedRenders", "1000"));
        var responses = await Task.WhenAll(requests.Select(r => Task.Run(() => client.PostAsJsonAsync("/render", r))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        for (var i = 0; i < responses.Length; i++)
        {
            var actual = await responses[i].Content.ReadAsByteArrayAsync();
            Assert.True(expected[i].AsSpan().SequenceEqual(actual), $"request {i} differed");
        }
    }

    [Fact]
    public async Task Overload_gives_only_correct_images_or_429s()
    {
        var requests = Requests();
        var expected = await RenderOneAtATime(requests);

        var client = FreshClient(("MaxConcurrentRenders", "2"), ("MaxQueuedRenders", "4"));
        var responses = await Task.WhenAll(requests.Select(r => Task.Run(() => client.PostAsJsonAsync("/render", r))));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.TooManyRequests, r.StatusCode.ToString()));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        for (var i = 0; i < responses.Length; i++)
        {
            if (responses[i].StatusCode != HttpStatusCode.OK)
                continue;
            var actual = await responses[i].Content.ReadAsByteArrayAsync();
            Assert.True(expected[i].AsSpan().SequenceEqual(actual), $"request {i} differed");
        }
    }

    [Fact]
    public void One_font_shared_by_many_threads_gives_the_same_glyphs_as_one_thread()
    {
        var codepoints = Enumerable.Range(0x21, 0x5E).Concat(Enumerable.Range(0xC0, 0x40)).ToArray();
        var path = TestPaths.Font("DejaVuSerif.ttf");
        var sequentialFont = new SixLaborsFontSource(path);
        var expected = codepoints.Select(c => Describe(sequentialFont.GetGlyph(c))).ToArray();

        var shared = new SixLaborsFontSource(path);  // deliberately uncached: every call reaches SixLabors
        using var start = new Barrier(16);
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        var threads = Enumerable.Range(0, 16).Select(t => new Thread(() =>
        {
            start.SignalAndWait();
            for (var round = 0; round < 5; round++)
            for (var i = 0; i < codepoints.Length; i++)
            {
                var index = (i * 7 + t) % codepoints.Length;  // different threads, different glyphs
                if (Describe(shared.GetGlyph(codepoints[index])) != expected[index])
                    failures.Add($"U+{codepoints[index]:X4} differed on thread {t}");
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.Empty(failures);
    }

    private static string Describe(Glyph glyph) =>
        $"{glyph.GlyphId}/{glyph.AdvanceWidth}/{glyph.LeftSideBearing}:" +
        string.Join(";", glyph.Contours.Select(c => c.Start + "|" + string.Join(",", c.Segments)));

    [Fact]
    public async Task Gate_never_runs_more_than_the_limit_at_once_and_does_reach_it()
    {
        using var gate = new RenderGate(new RenderLimits { MaxConcurrentRenders = 3, MaxQueuedRenders = 100 });
        var running = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(async () =>
        {
            using var lease = await gate.AcquireAsync(CancellationToken.None);
            Assert.True(lease.IsAcquired);

            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref running);
        })));

        Assert.Equal(3, peak);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
            if (Interlocked.CompareExchange(ref target, value, current) == current)
                return;
    }
}
