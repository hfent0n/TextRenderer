using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Service;

/// <summary>The real HTTP pipeline, in memory.</summary>
public class RenderApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> PostJson(string json, string contentType = "application/json") =>
        _client.PostAsync("/render", new StringContent(json, Encoding.UTF8, contentType));

    private static async Task<JsonElement> Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string[]> FieldErrors(HttpResponseMessage response, string field)
    {
        var problem = await Problem(response, HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out var errors),
            $"no '{field}' error in {problem}");
        return errors.EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    // --- POST /render: success ---

    [Fact]
    public async Task Renders_png_identical_to_calling_the_pipeline_directly()
    {
        var response = await PostJson("""{ "text": "Hello, World!", "font": "dejavu-serif", "fontSize": 32 }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);

        FontRegistry.Load("fonts").TryGet("dejavu-serif", out var font);
        var expected = PngEncoder.Encode(new RenderPipeline(font).Render("Hello, World!", new RenderOptions { FontSize = 32 }));
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Applies_every_option()
    {
        var response = await PostJson("""
            { "text": "Hi", "font": "dejavu-sans-mono", "fontSize": 20, "padding": 0, "foreground": "#ff0000", "background": "#0000FF" }
            """);

        FontRegistry.Load("fonts").TryGet("dejavu-sans-mono", out var font);
        var expected = PngEncoder.Encode(new RenderPipeline(font).Render("Hi", new RenderOptions
        {
            FontSize = 20, Padding = 0, Foreground = System.Drawing.Color.FromArgb(255, 0, 0), Background = System.Drawing.Color.FromArgb(0, 0, 255),
        }));
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Property_names_are_case_insensitive()
    {
        var response = await PostJson("""{ "TEXT": "Hi", "Font": "dejavu-sans", "FONTSIZE": 12 }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- POST /render: validation (400) ---

    [Fact]
    public async Task Validation_errors_are_problem_details_per_field()
    {
        var response = await PostJson("""{ "text": "a\n中", "font": "dejavu-sans", "fontSize": 0 }""");

        var problem = await Problem(response, HttpStatusCode.BadRequest);
        Assert.Equal("One or more validation errors occurred.", problem.GetProperty("title").GetString());
        var errors = problem.GetProperty("errors");
        Assert.Equal(["fontSize", "text"], errors.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(
            ["Control character U+000A is not allowed.", "Font 'dejavu-sans' has no glyph for U+4E2D '中'."],
            errors.GetProperty("text").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Unknown_property_is_named()
    {
        var errors = await FieldErrors(await PostJson("""{ "text": "Hi", "font": "dejavu-sans", "forground": "#ff0000" }"""), "forground");

        Assert.Equal(["Unknown property 'forground'."], errors);
    }

    [Theory]
    [InlineData("""{ "text": "Hi", "font": "dejavu-sans", "padding": "lots" }""", "padding")]
    [InlineData("""{ "text": "Hi", "font": "dejavu-sans", "padding": 8.5 }""", "padding")]
    [InlineData("""{ "text": 42, "font": "dejavu-sans" }""", "text")]
    [InlineData("""{ "text": "Hi", "font": ["dejavu-sans"] }""", "font")]
    public async Task Wrong_value_type_is_reported_on_its_field(string json, string field)
    {
        var errors = await FieldErrors(await PostJson(json), field);

        Assert.Equal([$"Invalid value for '{field}'."], errors);
    }

    [Theory]
    [InlineData("""{ "text": "Hi", """)]
    [InlineData("not json")]
    [InlineData("")]
    public async Task Malformed_json_is_reported_on_the_body(string json)
    {
        var errors = await FieldErrors(await PostJson(json), "body");

        Assert.StartsWith("Request body is not valid JSON", Assert.Single(errors));
    }

    [Theory]
    [InlineData("null")]
    public async Task Json_that_is_not_an_object_is_rejected(string json)
    {
        Assert.Equal(["Request body must be a JSON object."], await FieldErrors(await PostJson(json), "body"));
    }

    [Fact]
    public async Task Error_messages_do_not_leak_dotnet_type_names()
    {
        var responses = new[]
        {
            await PostJson("""{ "text": "Hi", "font": "dejavu-sans", "padding": "lots" }"""),
            await PostJson("""{ "text": "Hi", "font": "dejavu-sans", "colour": "#fff" }"""),
            await PostJson("""{ "text": """),
        };

        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("TextRenderer.", body);
            Assert.DoesNotContain("System.", body);
        }
    }

    // --- POST /render: 413 and 415 ---

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task Non_json_content_type_is_415(string contentType)
    {
        // Rejected by routing (the endpoint's Accepts metadata) before the handler runs.
        await Problem(await PostJson("""{ "text": "Hi", "font": "dejavu-sans" }""", contentType), HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Missing_content_type_is_415()
    {
        // Routing lets a request with no Content-Type through; the handler rejects it.
        var content = new ByteArrayContent("""{ "text": "Hi", "font": "dejavu-sans" }"""u8.ToArray());

        var problem = await Problem(await _client.PostAsync("/render", content), HttpStatusCode.UnsupportedMediaType);

        Assert.Equal("POST /render requires Content-Type: application/json.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Json_with_charset_is_accepted()
    {
        var content = new StringContent("""{ "text": "Hi", "font": "dejavu-sans" }""");
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");

        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/render", content)).StatusCode);
    }

    [Fact]
    public async Task Body_over_the_limit_is_413()
    {
        var json = $$"""{ "text": "{{new string('a', 17_000)}}", "font": "dejavu-sans" }""";

        var problem = await Problem(await PostJson(json), HttpStatusCode.RequestEntityTooLarge);

        Assert.Equal("The request body must be at most 16384 bytes.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Chunked_body_over_the_limit_is_413_even_without_content_length()
    {
        var bytes = Encoding.UTF8.GetBytes($$"""{ "text": "{{new string('a', 17_000)}}", "font": "dejavu-sans" }""");
        var content = new StreamContent(new NonSeekableStream(bytes));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await _client.PostAsync("/render", content);

        Assert.Null(content.Headers.ContentLength);
        await Problem(response, HttpStatusCode.RequestEntityTooLarge);
    }

    // --- GET /fonts, /health, unknown routes ---

    [Fact]
    public async Task Lists_fonts()
    {
        var fonts = await _client.GetFromJsonAsync<JsonElement>("/fonts");

        Assert.Equal(
            """[{"id":"dejavu-sans","family":"DejaVu Sans","style":"Regular","unitsPerEm":2048},{"id":"dejavu-sans-mono","family":"DejaVu Sans Mono","style":"Regular","unitsPerEm":2048},{"id":"dejavu-serif","family":"DejaVu Serif","style":"Regular","unitsPerEm":2048}]""",
            fonts.GetRawText());
    }

    [Fact]
    public async Task Health_is_ok()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_route_is_404_problem_details()
    {
        await Problem(await _client.GetAsync("/nope"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Wrong_method_is_405()
    {
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.GetAsync("/render")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_documents_the_endpoints()
    {
        var doc = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        var paths = doc.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/render", paths);
        Assert.Contains("/fonts", paths);
    }

    [Fact]
    public async Task The_documented_example_request_renders()
    {
        var doc = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var example = doc.GetProperty("components").GetProperty("schemas").GetProperty("RenderRequest").GetProperty("example");

        var response = await PostJson(example.GetRawText());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ui_is_served_in_development()
    {
        var response = await _client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ui_and_openapi_are_not_served_in_production()
    {
        using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var client = production.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    // --- 500 ---

    private sealed class ExplodingFontSource : IFontSource
    {
        public FontMetrics Metrics { get; } = new(64, 48, -16);
        public bool HasGlyph(int codepoint) => true;
        public Glyph GetGlyph(int codepoint) => throw new InvalidOperationException("secret internal detail");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Unexpected_errors_are_500_problem_details_without_internals(string environment)
    {
        using var broken = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.ConfigureServices(s => s.AddSingleton(FontRegistry.Create(
                [(new FontInfo("exploding", "Exploding", "Regular", 64), new ExplodingFontSource())])));
        });

        var response = await broken.CreateClient().PostAsync("/render",
            new StringContent("""{ "text": "Hi", "font": "exploding" }""", Encoding.UTF8, "application/json"));

        var problem = await Problem(response, HttpStatusCode.InternalServerError);
        var body = problem.GetRawText();
        Assert.DoesNotContain("secret internal detail", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain(" at ", body);
    }

    // --- startup fails fast ---

    [Fact]
    public void Nonsensical_limits_stop_startup()
    {
        using var bad = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Render:MinFontSize", "50");
            b.UseSetting("Render:MaxFontSize", "10");
        });

        var ex = Assert.ThrowsAny<Exception>(() => bad.CreateClient());

        Assert.Contains("MaxFontSize must be at least MinFontSize", ex.ToString());
    }

    [Fact]
    public void Missing_font_directory_stops_startup()
    {
        using var bad = factory.WithWebHostBuilder(b => b.UseSetting("Render:FontDirectory", "no-such-fonts"));

        var ex = Assert.ThrowsAny<Exception>(() => bad.CreateClient());

        Assert.Contains("does not exist", ex.ToString());
    }

    /// <summary>Forces HttpClient to send without Content-Length.</summary>
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
