using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TextRenderer.Rendering;
using TextRenderer.Service;

namespace TextRenderer.Tests.Service;

public class TelemetryTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Secret = "TOP-SECRET-TEXT";

    private WebApplicationFactory<Program> App(Action<IServiceCollection>? services = null, params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings)
                b.UseSetting($"Render:{key}", value);
            b.ConfigureServices(s =>
            {
                s.AddFakeLogging();
                services?.Invoke(s);
            });
        });

    private static Task<HttpResponseMessage> Post(WebApplicationFactory<Program> app, string json) =>
        app.CreateClient().PostAsync("/render", new StringContent(json, Encoding.UTF8, "application/json"));

    private static MetricCollector<double> Durations(WebApplicationFactory<Program> app) =>
        new(app.Services.GetRequiredService<IMeterFactory>(), RenderTelemetry.MeterName, RenderTelemetry.RenderDurationMetric);

    private static FakeLogRecord Entry(WebApplicationFactory<Program> app) =>
        Assert.Single(app.Services.GetFakeLogCollector().GetSnapshot(), r => r.Category == typeof(RenderTelemetry).FullName);

    private static void AssertNoLogContains(WebApplicationFactory<Program> app, string value) =>
        Assert.DoesNotContain(app.Services.GetFakeLogCollector().GetSnapshot(), r =>
            r.Message.Contains(value) || r.StructuredState?.Any(kv => kv.Value?.Contains(value) == true) == true);

    [Fact]
    public async Task A_render_logs_one_entry_and_records_its_duration_without_the_text()
    {
        using var app = App();
        using var durations = Durations(app);

        var response = await Post(app, $$"""{ "text": "{{Secret}}", "font": "dejavu-sans", "fontSize": 20 }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Entry(app);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("dejavu-sans", entry.GetStructuredStateValue("Font"));
        Assert.Equal("20", entry.GetStructuredStateValue("FontSize"));
        Assert.Equal(Secret.Length.ToString(), entry.GetStructuredStateValue("TextLength"));
        Assert.NotNull(entry.GetStructuredStateValue("Width"));
        Assert.NotNull(entry.GetStructuredStateValue("RenderMs"));
        AssertNoLogContains(app, Secret);

        var measurement = Assert.Single(durations.GetMeasurementSnapshot());
        Assert.True(measurement.Value > 0);
        Assert.Equal("rendered", measurement.Tags["outcome"]);
        Assert.Equal("dejavu-sans", measurement.Tags["font"]);
    }

    [Fact]
    public async Task A_rejected_request_logs_its_status_but_nothing_the_caller_typed()
    {
        using var app = App();

        var response = await Post(app, $$"""{ "text": "{{Secret}}", "font": "{{Secret}}" }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("400", Entry(app).GetStructuredStateValue("Status"));
        AssertNoLogContains(app, Secret);
    }

    [Fact]
    public async Task A_timeout_logs_a_warning_and_records_its_outcome()
    {
        using var app = App(
            s => s.AddSingleton(FontRegistry.Create(
                [(new FontInfo("slow", "Slow", "Regular", 64), new SlowFontSource(TimeSpan.FromMilliseconds(30)))])),
            ("RenderTimeout", "00:00:00.050"));
        using var durations = Durations(app);

        var response = await Post(app, """{ "text": "AAAAAAAAAA", "font": "slow", "fontSize": 16 }""");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(LogLevel.Warning, Entry(app).Level);
        Assert.Equal("timed_out", Assert.Single(durations.GetMeasurementSnapshot()).Tags["outcome"]);
    }
}
