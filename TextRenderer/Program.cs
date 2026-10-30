using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TextRenderer.Rendering;
using TextRenderer.Service;

var builder = WebApplication.CreateBuilder(args);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options => options.AddSchemaTransformer((schema, context, _) =>
{
    // Prefills Swagger's "Try it out" with a request that works.
    if (context.JsonTypeInfo.Type == typeof(RenderRequest))
        schema.Example = JsonNode.Parse("""{ "text": "Hello, World!", "font": "dejavu-sans", "fontSize": 48 }""");
    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();

builder.Services.AddOptions<RenderLimits>()
    .Bind(builder.Configuration.GetSection(RenderLimits.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<RenderLimits>, RenderLimitsValidation>();

builder.Services.AddSingleton(sp => FontRegistry.Load(sp.GetRequiredService<IOptions<RenderLimits>>().Value.FontDirectory));
builder.Services.AddSingleton(sp => new RenderRequestValidator(
    sp.GetRequiredService<FontRegistry>(), sp.GetRequiredService<IOptions<RenderLimits>>().Value));

builder.Services.AddSingleton(sp => new RenderGate(sp.GetRequiredService<IOptions<RenderLimits>>().Value));

builder.Services.AddSingleton(sp => new RenderCache(
    sp.GetRequiredService<FontRegistry>(), sp.GetRequiredService<IOptions<RenderLimits>>().Value));
builder.Services.AddSingleton<RenderTelemetry>();

builder.Services.AddHealthChecks();

var app = builder.Build();

// Fail fast: validate limits and load every font before accepting requests (PLAN_PART_2 decisions 10 and 15).
app.Services.GetRequiredService<FontRegistry>();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "TextRenderer"));
}

app.UseHttpsRedirection();

app.MapRenderEndpoints();
app.MapHealthChecks("/health");

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
